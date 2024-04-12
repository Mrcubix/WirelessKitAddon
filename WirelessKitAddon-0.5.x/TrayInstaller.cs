using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin;
using WirelessKitAddon.Extensions;

namespace WirelessKitAddon.Lib
{
    public class TrayManager : IDisposable
    {
        private readonly static Assembly _assembly = Assembly.GetExecutingAssembly();
        private readonly static FileInfo _file = new(_assembly.Location);
        private readonly DirectoryInfo? _directory = _file.Directory;

        private readonly TimeSpan _timeout = TimeSpan.FromMinutes(10);

        private static readonly string _rid = RuntimeInformation.RuntimeIdentifier;
        private static readonly string _extension = RuntimeInformation.ExecutableExtension;
        private readonly string _zipFilename = $"WirelessKitBatteryStatus.UX-{_rid}.zip";
        private readonly string _filename = $"WirelessKitBatteryStatus.UX{_extension}";

        private string _versionFilePath = string.Empty;
        private string _dateFilePath = string.Empty;
        private string _appPath = string.Empty;

        public TrayManager()
        {
            if (_directory == null)
            {
                Log.Write("Wireless Kit Addon", "Could not find the parent folder of this plugin.", LogLevel.Error);
                return;
            }

            _versionFilePath = Path.Combine(_directory.FullName, "version.txt");
            _dateFilePath = Path.Combine(_directory.FullName, "date.txt");
            _appPath = Path.Combine(_directory.FullName, _filename);
        }

        public bool IsReady { get; private set; } = false;

        public async Task<bool> Setup()
        {
            if (_directory == null)
                return false;

            try
            {
                if (!await Download())
                    Log.Write("Wireless Kit Addon", "The latest version of the Wireless Kit Addon is already installed.", LogLevel.Info);
            }
            catch (Exception)
            {
                Log.Write("Wireless Kit Addon", $"An error occurred while downloading the latest version of the tray icon, attempting to continue...", LogLevel.Warning);
            }

            if (!File.Exists(_appPath))
            {
                Log.Write("Wireless Kit Addon", "The tray icon could not be found, cannot continue.", LogLevel.Error);
                return false;
            }

            IsReady = true;

            return true;
        }

        public bool Start(string tabletName)
        {
            if (!IsReady)
                return false;

            // Run the tray icon
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _appPath,
                    UseShellExecute = true
                };

                startInfo.ArgumentList.Add(tabletName);

                var process = new Process
                {
                    StartInfo = startInfo
                };

                process.Start();
            }
            catch (Exception)
            {
                Log.Write("Wireless Kit Addon", "An error occurred while starting the tray icon, attempting to continue...", LogLevel.Warning);
            }

            return true;
        }

        private async Task<bool> Download()
        {
            // Download the latest version of the Wireless Kit Addon from the GitHub repository
            // and extract the contents to the parent folder of the plugin

            var lastDownloadedSerialized = File.Exists(_dateFilePath) ? File.ReadAllText(_dateFilePath) : string.Empty;
            var hasBeenDownloaded = DateTime.TryParse(lastDownloadedSerialized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);

            // Check if the file as been downloaded in the past 10 minutes, and it is the case, skip it
            if (hasBeenDownloaded && File.Exists(_filename) && date - DateTime.Now < _timeout)
                return false;

            var url = $"https://github.com/Mrcubix/WirelessKitAddon/releases/latest/download/{_zipFilename}";
            var versionUrl = "https://github.com/Mrcubix/WirelessKitAddon/releases/latest/download/version.txt";

            using var client = new HttpClient();

            if (client == null)
                return false;

            var current = File.Exists(_versionFilePath) ? File.ReadAllText(_versionFilePath) : string.Empty;
            var version = await client.GetStringFromFile(versionUrl);

            byte[] data;

            // Check to see if the version is up to date
            data = await client.DownloadFile(versionUrl);

            Log.Write("Wireless Kit Addon", $"Checking for updates...", LogLevel.Info);

            if (version == current)
                return false;

            Log.Write("Wireless Kit Addon", $"Downloading the latest version of the Wireless Kit Addon...", LogLevel.Info);

            var downloadPath = Path.Combine(_directory!.FullName, _filename);

            data = await client.DownloadFile(url);
            using MemoryStream stream = new(data);
            using ZipArchive archive = new(stream);

            archive.ExtractToDirectory(_directory.FullName, true);

            File.WriteAllText(_versionFilePath, version);
            File.WriteAllText(_dateFilePath, DateTime.Now.ToString(CultureInfo.InvariantCulture));

            return true;
        }

        public void Dispose()
        {
            Process.GetProcessesByName(_filename).FirstOrDefault()?.Kill();
            GC.SuppressFinalize(this);
        }
    }
}