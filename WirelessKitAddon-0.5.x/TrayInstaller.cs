using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin;
using WirelessKitAddon.Extensions;

namespace WirelessKitAddon
{
    public class TrayInstaller
    {
        private static Assembly _assembly = Assembly.GetExecutingAssembly();
        private static FileInfo _file = new(_assembly.Location);
        private static DirectoryInfo? _directory = _file.Directory;

        private static TimeSpan _timeout = TimeSpan.FromMinutes(10);

        private static readonly string _rid = RuntimeInformation.RuntimeIdentifier;
        private static readonly string _extension = RuntimeInformation.ExecutableExtension;
        private static readonly string _filename = $"WirelessKitBatteryStatus.UX-{_rid}{_extension}";

        private static string _versionFilePath = string.Empty;
        private static string _dateFilePath = string.Empty;
        private static string _appPath = string.Empty;

        public static async Task Setup()
        {
            if (_directory == null)
            {
                Log.Write("Wireless Kit Addon", "Could not find the parent folder of this plugin.", LogLevel.Error);
                return;
            }

            _versionFilePath = Path.Combine(_directory.FullName, "version.txt");
            _dateFilePath = Path.Combine(_directory.FullName, "date.txt");
            _appPath = Path.Combine(_directory.FullName, _filename);

            try
            {
                if (!await Download())
                {
                    Log.Write("Wireless Kit Addon", "The latest version of the Wireless Kit Addon is already installed.", LogLevel.Info);
                    return;
                }
            }
            catch (Exception)
            {
                Log.Write("Wireless Kit Addon", $"An error occurred while downloading the latest version of the tray icon, attempting to continue...", LogLevel.Warning);
            }

            if (!File.Exists(_appPath))
            {
                Log.Write("Wireless Kit Addon", "The tray icon could not be found, cannot continue.", LogLevel.Error);
                return;
            }

            // Run the tray icon
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _appPath,
                        UseShellExecute = true
                    }
                };

                process.Start();
            }
            catch (Exception)
            {
                Log.Write("Wireless Kit Addon", "An error occurred while starting the tray icon, attempting to continue...", LogLevel.Warning);
            }
        }

        private static async Task<bool> Download()
        {
            // Download the latest version of the Wireless Kit Addon from the GitHub repository
            // and extract the contents to the parent folder of the plugin

            var lastDownloadedSerialized = File.Exists(_dateFilePath) ? File.ReadAllText(_dateFilePath) : string.Empty;
            var hasBeenDownloaded = DateTime.TryParse(lastDownloadedSerialized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);

            // Check if the file as been downloaded in the past 10 minutes, and it is the case, skip it
            if (hasBeenDownloaded && File.Exists(_filename) && date - DateTime.Now < _timeout)
                return false;

            var url = $"https://github.com/Mrcubix/WirelessKitAddon/releases/latest/download/{_filename}";
            var versionUrl = "https://github.com/Mrcubix/WirelessKitAddon/releases/latest/download/version.txt";

            using var client = new HttpClient();

            if (client == null)
                return false;

            var current = File.Exists(_versionFilePath) ? File.ReadAllText(_versionFilePath) : string.Empty;
            var version = await client.GetStringFromFile(versionUrl);

            byte[] data;

            // Check to see if the version is up to date
            data = await client.DownloadFile(versionUrl);

            if (version == current)
                return false;

            var downloadPath = Path.Combine(_directory!.FullName, _filename);

            data = await client.DownloadFile(url);
            File.WriteAllBytes(downloadPath, data);
            
            File.WriteAllText(_versionFilePath, version);
            File.WriteAllText(DateTime.Now.ToString(CultureInfo.InvariantCulture), _dateFilePath);

            return true;
        }
    }
}