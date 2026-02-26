using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Plugin;
using WirelessKitAddon.Extensions;

namespace WirelessKitAddon.Lib
{
    public class TrayManager : IDisposable
    {
        private readonly DirectoryInfo? _directory = GetPluginDirectory();

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

            // Ensure the binary is executable on Unix-based systems
            if (!OperatingSystem.IsWindows())
                SetExecutablePermission(_appPath);

            // On macOS (Apple Silicon), overwriting a Mach-O binary invalidates its
            // adhoc code signature. macOS kills unsigned binaries with SIGKILL (exit 137).
            // Re-sign with an adhoc signature so the OS allows execution.
            if (OperatingSystem.IsMacOS())
                AdhocSign(_appPath);

            IsReady = true;

            return true;
        }

        public bool Start(string tabletName)
        {
            if (!IsReady)
                return false;

            string filename;
            string arguments;

            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                filename = _appPath;
                arguments = $"\"{tabletName}\"";
            }
            else
            {
                Log.Write("Wireless Kit Addon", "The tray icon is not supported on this operating system.", LogLevel.Error);
                return false;
            }

            // Run the tray icon
            try
            {
                var process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = filename,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                // Read streams asynchronously to avoid deadlock
                string? stdout = null;
                string? stderr = null;
                var stdoutTask = Task.Run(() => stdout = process.StandardOutput.ReadToEnd());
                var stderrTask = Task.Run(() => stderr = process.StandardError.ReadToEnd());

                _ = Task.Run(() =>
                {
                    var res = process.WaitForExit(2000);

                    if (res) // Exited due to an error
                    {
                        stdoutTask.Wait(1000);
                        stderrTask.Wait(1000);
                        Log.Write("Wireless Kit Addon", $"The tray icon has exited unexpectedly:\n" +
                                                       $"stdout: {stdout}\nstderr: {stderr}", LogLevel.Error);
                    }
                    else
                        Log.Write("Wireless Kit Addon", "The tray icon has been started successfully.", LogLevel.Info);
                });
            }
            catch (Exception e)
            {
                Log.Write("Wireless Kit Addon", "An exception occured while starting the tray icon: \n" +
                                               $"Exception: {e.Message}", LogLevel.Error);
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

            // On Unix-based systems, set the executable permission on the extracted binary
            if (!OperatingSystem.IsWindows())
                SetExecutablePermission(_appPath);

            File.WriteAllText(_versionFilePath, version);
            File.WriteAllText(_dateFilePath, DateTime.Now.ToString(CultureInfo.InvariantCulture));

            return true;
        }

        private static DirectoryInfo? GetPluginDirectory()
        {
            var pluginsRoot = AppInfo.Current?.PluginDirectory;
            if (pluginsRoot == null || !Directory.Exists(pluginsRoot))
                return null;

            foreach (var dir in Directory.GetDirectories(pluginsRoot))
            {
                if (File.Exists(Path.Combine(dir, "WirelessKitAddon.Lib.dll")))
                    return new DirectoryInfo(dir);
            }

            return null;
        }

        private static void SetExecutablePermission(string filePath)
        {
            try
            {
                var process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{filePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(5000);
            }
            catch (Exception e)
            {
                Log.Write("Wireless Kit Addon", $"Failed to set executable permission: {e.Message}", LogLevel.Warning);
            }
        }

        private static void AdhocSign(string filePath)
        {
            try
            {
                var process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "codesign",
                        Arguments = $"--force --sign - \"{filePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(5000);
            }
            catch (Exception e)
            {
                Log.Write("Wireless Kit Addon", $"Failed to adhoc sign binary: {e.Message}", LogLevel.Warning);
            }
        }

        public void Dispose()
        {
            Process.GetProcessesByName(_filename).FirstOrDefault()?.Kill();
            GC.SuppressFinalize(this);
        }
    }
}