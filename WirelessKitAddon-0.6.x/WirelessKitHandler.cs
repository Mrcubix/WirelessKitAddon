using HidSharp;
using OpenTabletDriver;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.DependencyInjection;
using WirelessKitAddon.Parsers;
using OpenTabletDriver.Plugin.Devices;
using System.Collections.Immutable;
using WirelessKitAddon.Lib;
using WirelessKitAddon.Interfaces;
using WirelessKitAddon.Native;

#if OTD06

namespace WirelessKitAddon
{
    [PluginName("Wireless Kit Addon")]
    public class WirelessKitHandler : WirelessKitHandlerBase, IPositionedPipelineElement<IDeviceReport>, IDisposable
    {
        #region Constants

        private const ushort WACOM_VID = 1386;
        private const ushort WIRELESS_KIT_PID = 132;
        private const ushort WIRELESS_KIT_IDENTIFIER_INPUT_LENGTH = 32;
        private const ushort WIRELESS_KIT_IDENTIFIER_OUTPUT_LENGTH = 259;

        private const byte POWER_SAVING_MIN_TIMEOUT = 1;
        private const byte POWER_SAVING_MAX_TIMEOUT = 20;

        // The PID of all supported tablets
        private readonly ImmutableArray<int> SUPPORTED_TABLETS = ImmutableArray.Create<int>(
            38, 209, 210, 211, 214, 215, 219, 222, 223, 770, 771, 828, 830
        );

        private readonly DeviceIdentifier wirelessKitIdentifier = new()
        {
            VendorID = WACOM_VID,
            ProductID = WIRELESS_KIT_PID,
            InputReportLength = WIRELESS_KIT_IDENTIFIER_INPUT_LENGTH,
            OutputReportLength = null,
            ReportParser = typeof(WirelessReportParser).FullName ?? string.Empty,
            FeatureInitReport = new List<byte[]> { new byte[2] { 0x02, 0x02 } },
            OutputInitReport = null,
            DeviceStrings = new(),
            InitializationStrings = new()
        };

        #endregion

        #region Fields

        private readonly byte[] _powerSavingReport = new byte[13] { 0x03, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 };

        private bool _isWireless;

        private bool _pastEarlyWarning;
        private bool _pastLateWarning;

        private CancellationTokenSource? _presenceCts;

        private TabletReference? _tablet;

        private InputDevice? _device;
        private IOKitBatteryReader? _iokitReader;

        #endregion

        #region Initialization

        public WirelessKitHandler() : base()
        {
            WirelessKitDaemonBase.Ready += OnDaemonReady;
        }

        public void Initialize(IDriver? driver, TabletReference? tablet)
        {
            // instantly remove the event handler to prevent multiple initializations
            WirelessKitDaemonBase.Ready -= OnDaemonReady;

            if (driver is Driver _driver && tablet != null)
            {
                // Need to fetch the tree first to obtain the output mode
                var tabletTree = _driver.InputDevices.FirstOrDefault(tree => tree.Properties == tablet.Properties);
                OutputMode = tabletTree?.OutputMode;

                if (OutputMode == null)
                    return;

                // Try wired first: if the tablet's wired USB device is present, use it
                HandleWiredTablet(_driver);

                // If wired device wasn't found, the tablet must be communicating over the wireless dongle
                if (DeviceTree == null)
                {
                    HandleWirelessKit(_driver);
                }

                // If we still couldn't open the standard 32-byte battery endpoint (e.g. on macOS
                // where HidSharp reports InputLen=0 for the wireless monitor interface because the
                // HID descriptor marks Report ID 0x80 as Input (Constant)), try the IOKit reader
                // which bypasses HidSharp and reads battery reports directly via IOKit callbacks.
                if (DeviceTree == null && _driver.CompositeDeviceHub.GetDevices()
                    .Any(d => d.VendorID == WACOM_VID && d.ProductID == WIRELESS_KIT_PID))
                {
                    _isWireless = true;

                    if (OperatingSystem.IsMacOS())
                    {
                        try
                        {
                            _iokitReader = new IOKitBatteryReader();
                            if (_iokitReader.Start())
                            {
                                _iokitReader.Report += OnIOKitReport;
                                _iokitReader.ReadingChanged += OnConnectionStateChanged;
                                Log.Write("Wireless Kit Addon",
                                    "Using wireless mode (macOS IOKit battery reader).", LogLevel.Info);
                            }
                            else
                            {
                                _iokitReader.Dispose();
                                _iokitReader = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            _iokitReader?.Dispose();
                            _iokitReader = null;
                            Log.Write("Wireless Kit Addon",
                                $"IOKit battery reader error: {ex.Message}", LogLevel.Warning);
                        }
                    }

                    if (_iokitReader != null)
                    {
                        // Don't call OnTabletDetected() yet — the dongle sends reports
                        // even when the tablet is powered off (with IsConnected = false).
                        // OnIOKitReport will call OnTabletDetected() when the first report
                        // with IsConnected = true arrives.
                        Log.Write("Wireless Kit Addon",
                            "IOKit reader active — waiting for tablet connection...", LogLevel.Info);
                    }
                    else
                    {
                        Log.Write("Wireless Kit Addon",
                            "Dongle detected — checking for tablet presence...", LogLevel.Info);

                        if (IsTabletBehindDongle())
                        {
                            OnTabletDetected();
                        }
                        else
                        {
                            Log.Write("Wireless Kit Addon",
                                "Dongle present but no tablet connected. Monitoring...", LogLevel.Info);
                            StartPresenceMonitor();
                        }
                    }
                }

                if (DeviceTree == null && !_isWireless)
                    Log.Write("Wireless Kit Addon", $"Failed to handle the Wireless Kit for {tablet.Properties.Name}", LogLevel.Warning);
                else if (DeviceTree != null)
                {
                    // Wired or wireless (non-passthrough) mode — standard initialization
                    BringToDaemon();

                    if (_daemon != null && _instance != null)
                    {
                        _ = Task.Run(SetupTrayIcon);

                        var mode = _isWireless ? "wireless dongle" : "USB";
                        var name = _instance.Name;
                        Log.Write("Wireless Kit Addon", $"Now handling Wireless Kit Reports for {name}", LogLevel.Info);

                        var delay = Math.Max(NotificationTimeout, 1) * 1000;
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(delay);
                            Log.WriteNotify("Wireless Kit Addon", $"{name} connected through {mode}.", LogLevel.Info);
                        });
                    }
                }
            }
        }

        protected override void HandleWirelessKit(Driver driver)
        {
            // Try the standard 32-byte battery endpoint (works on Linux/Windows)
            var matches = GetMatchingDevices(driver, Tablet!.Properties, wirelessKitIdentifier).ToList();

            if (matches.Count > 0)
            {
                _isWireless = true;
                Log.Write("Wireless Kit Addon", "Using wireless mode (32-byte dongle endpoint).", LogLevel.Info);
                HandleMatch(driver, matches);
            }

            // On macOS, HidSharp reports InputLen=0 for the wireless monitor interface
            // because the HID descriptor marks Report ID 0x80 as Input (Constant).
            // The IOKit battery reader (IOKitBatteryReader) handles this case in the
            // passthrough path below by using IOKit callbacks directly.
        }

        protected override void HandleWiredTablet(Driver driver)
        {
            // We need to find a wired device (not the wireless dongle) with input length 10 or 11
            var tree = driver.InputDevices.FirstOrDefault(tree => tree.Properties == Tablet!.Properties);
            var device = tree?.InputDevices.FirstOrDefault(device =>
                (device.Identifier.InputReportLength == 10 || device.Identifier.InputReportLength == 11) &&
                device.Identifier.ProductID != WIRELESS_KIT_PID);

            if (device == null)
                return;

            // Verify the wired device actually exists on the USB bus
            var matches = GetMatchingDevices(driver, Tablet!.Properties, device.Identifier).ToList();

            if (matches.Count == 0)
                return;

            Log.Write("Wireless Kit Addon", "Using wired mode (USB).", LogLevel.Info);

            HandleMatch(driver, matches);
        }

        private bool HandleMatch(Driver driver, IEnumerable<IDeviceEndpoint> matches)
        {
            if (matches.Count() > 1)
                Log.Write("Wireless Kit Addon", "Multiple devices matched the Wireless Kit identifier. This is unexpected.", LogLevel.Warning);

            foreach (var match in matches)
            {
                if (match == null)
                    continue;

                try
                {
                    // Create a device from the match & start checking if it disconnects
                    _device = new InputDevice(driver, match, Tablet!.Properties, wirelessKitIdentifier);
                    _device.ConnectionStateChanged += OnConnectionStateChanged;
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, LogLevel.Warning);
                    continue;
                }

                DeviceTree = new InputDeviceTree(Tablet.Properties, new List<InputDevice> { _device })
                {
                    OutputMode = OutputMode
                };
            }

            if (_isWireless)
                SetBatterySavingModeTimeout();

            return DeviceTree != null;
        }

        #endregion

        #region Events

        public event Action<IDeviceReport>? Emit;

        #endregion

        #region Properties

        [Resolved]
        public IDriver? Driver
        {
            get => _driver;
            set => _driver = value;
        }

        [TabletReference]
        public TabletReference? Tablet
        {
            get => _tablet;
            set => _tablet = value;
        }

        public PipelinePosition Position => PipelinePosition.PreTransform;

        /// <summary>
        ///   The device tree that will be used to emit reports.
        /// </summary>
        public InputDeviceTree? DeviceTree { get; set; }

        /// <summary>
        ///   The output mode of the tablet.
        /// </summary>
        public IOutputMode? OutputMode { get; set; }

        #endregion

        #region Methods

        public void Consume(IDeviceReport report)
        {
            if (_instance != null)
            {
                if (report is IBatteryReport batteryReport)
                {
                    _instance.BatteryLevel = batteryReport.Battery;
                    _instance.IsCharging = batteryReport.IsCharging;

                    CheckBatteryWarnings(batteryReport.Battery, batteryReport.IsCharging);
                }

                if (report is IWirelessKitReport wirelessReport)
                    _instance.IsConnected = wirelessReport.IsConnected;
                else
                    _instance.IsConnected = true;

                _daemon?.Update(_instance);
            }

            Emit?.Invoke(report);
        }

        private void CheckBatteryWarnings(float battery, bool isCharging)
        {
            if (isCharging)
            {
                // Reset warnings when charging so they fire again after unplug
                _pastEarlyWarning = false;
                _pastLateWarning = false;
                return;
            }

            if (LateWarningSetting >= 0 && battery <= LateWarningSetting && !_pastLateWarning)
            {
                _pastLateWarning = true;
                Log.WriteNotify("Wireless Kit Addon",
                    $"{_instance!.Name}: Battery critically low ({battery}%)!",
                    LogLevel.Warning);
            }
            else if (EarlyWarningSetting >= 0 && battery <= EarlyWarningSetting && !_pastEarlyWarning)
            {
                _pastEarlyWarning = true;
                Log.WriteNotify("Wireless Kit Addon",
                    $"{_instance!.Name}: Battery low ({battery}%).",
                    LogLevel.Warning);
            }
        }

        public override void BringToDaemon()
        {
            if (_daemon == null)
                return;

            _instance = new WirelessKitInstance(_tablet!.Properties.Name, false, 0, false, EarlyWarningSetting, LateWarningSetting, NotificationTimeout);

            _daemon.Add(_instance);
        }

        public override void SetBatterySavingModeTimeout()
        {
            if (_device == null)
                return;

            _powerSavingReport[2] = Math.Clamp((byte)PowerSavingTimeout, POWER_SAVING_MIN_TIMEOUT, POWER_SAVING_MAX_TIMEOUT);

            try
            {
                _device.ReportStream.SetFeature(_powerSavingReport);
            }
            catch (Exception)
            {
                Log.Write("Wireless Kit Addon", $"Failed to set the power saving mode timeout.", LogLevel.Error);
            }
        }

        /// <summary>
        ///   Check if a tablet is actually connected behind the wireless dongle by reading
        ///   from the 64-byte aux/express key endpoint. The dongle streams reports on this
        ///   endpoint only when a tablet is paired and powered on.
        /// </summary>
        private static bool IsTabletBehindDongle()
        {
            var auxDevice = DeviceList.Local
                .GetHidDevices(WACOM_VID, WIRELESS_KIT_PID)
                .FirstOrDefault(d => d.GetMaxInputReportLength() == 64);

            if (auxDevice == null || !auxDevice.TryOpen(out var stream))
                return false;

            try
            {
                stream.ReadTimeout = 2000;
                var buf = new byte[64];
                stream.Read(buf, 0, buf.Length);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            finally
            {
                stream.Dispose();
            }
        }

        /// <summary>
        ///   Start a background polling loop that checks for tablet presence every 3 seconds.
        ///   When a tablet is detected, calls <see cref="OnTabletDetected"/>.
        /// </summary>
        private void StartPresenceMonitor()
        {
            _presenceCts = new CancellationTokenSource();
            var token = _presenceCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(3000, token);

                    if (IsTabletBehindDongle())
                    {
                        OnTabletDetected();
                        return;
                    }
                }
            }, token);
        }

        /// <summary>
        ///   Called when a tablet is detected behind the dongle (either immediately or
        ///   after polling). Sets up the daemon, tray icon, and connection notification.
        /// </summary>
        private void OnTabletDetected()
        {
            Log.Write("Wireless Kit Addon",
                "Tablet detected behind wireless dongle.", LogLevel.Info);

            BringToDaemon();

            if (_daemon != null && _instance != null)
            {
                // Only set battery to unknown if no reader is active
                if (_iokitReader == null)
                    _instance.BatteryLevel = -1;

                _ = Task.Run(SetupTrayIcon);

                var name = _instance.Name;
                var mode = _iokitReader != null ? "wireless dongle (IOKit)" : "wireless dongle";
                Log.Write("Wireless Kit Addon", $"Now handling Wireless Kit Reports for {name}", LogLevel.Info);

                var delay = Math.Max(NotificationTimeout, 1) * 1000;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delay);
                    Log.WriteNotify("Wireless Kit Addon",
                        $"{name} connected through {mode}.", LogLevel.Info);
                });
            }
        }

        /// <summary>
        ///   Called when the tablet is powered off while the IOKit reader is active.
        ///   Tears down the daemon instance, tray icon, and shows a disconnect notification,
        ///   but keeps the IOKit reader alive so it can detect the tablet powering back on.
        /// </summary>
        private void OnTabletPoweredOff()
        {
            var name = _instance?.Name ?? "Tablet";

            if (_instance != null && _daemon != null)
            {
                _daemon.Remove(_instance);
                Log.Write("Wireless Kit Addon",
                    $"Stopped handling Wireless Kit Reports for {name}", LogLevel.Info);
                _instance = null;
            }

            _trayManager?.Dispose();
            _trayManager = null;

            // Reset battery warnings so they fire again on reconnect
            _pastEarlyWarning = false;
            _pastLateWarning = false;

            Log.WriteNotify("Wireless Kit Addon",
                $"{name} disconnected.", LogLevel.Info);
        }

        #endregion

        #region Event Handlers

        private void OnDaemonReady(object? sender, EventArgs e)
        {
            _daemon = WirelessKitDaemonBase.Instance;

            if (_instance == null)
                Initialize(_driver, _tablet);
        }

        private void OnConnectionStateChanged(object? sender, bool connected)
        {
            if (connected == false) // Pre-Dispose on disconnect
                StopHandling();
        }

        /// <summary>
        ///   Handles reports from the macOS IOKit battery reader.
        ///   Detects tablet power-on/off transitions via IsConnected and starts or
        ///   tears down the daemon, tray icon, and notification accordingly.
        ///   The IOKit reader itself stays alive across power cycles so it can
        ///   detect the tablet powering back on.
        /// </summary>
        private void OnIOKitReport(object? sender, IDeviceReport report)
        {
            if (report is IWirelessKitReport wirelessReport)
            {
                if (wirelessReport.IsConnected && _instance == null)
                {
                    // Tablet powered on (or first connected report)
                    OnTabletDetected();
                }
                else if (!wirelessReport.IsConnected && _instance != null)
                {
                    // Tablet powered off — tear down services but keep IOKit reader alive
                    OnTabletPoweredOff();
                }
            }

            Consume(report);
        }

        #endregion

        #region Static Methods

        private static IEnumerable<IDeviceEndpoint> GetMatchingDevices(Driver driver, TabletConfiguration configuration, DeviceIdentifier identifier)
        {
            return from device in driver.CompositeDeviceHub.GetDevices()
                   where identifier.VendorID == device.VendorID
                   where identifier.ProductID == device.ProductID
                   where device.CanOpen
                   where identifier.InputReportLength == null || identifier.InputReportLength == device.InputReportLength
                   where identifier.OutputReportLength == null || identifier.OutputReportLength == device.OutputReportLength
                   select device;
        }

        #endregion

        #region Disposal

        private void StopHandling()
        {
            // Daemon remove will just be replaced by a rpc dispose when the daemon will get nuked
            if (_instance != null && _daemon != null)
            {
                _daemon.Remove(_instance);
                Log.Write("Wireless Kit Addon", $"Stopped handling Wireless Kit Reports for {_instance.Name}", LogLevel.Info);
                _instance = null;
            }

            // Dispose of the devices themselves,
            // No need to dispose of the output mode as it's not owned by us
            if (DeviceTree != null)
            {
                foreach (var device in DeviceTree.InputDevices)
                {
                    device.ConnectionStateChanged -= OnConnectionStateChanged;
                    device.Dispose();
                }

                DeviceTree = null;
            }

            if (_iokitReader != null)
            {
                _iokitReader.Report -= OnIOKitReport;
                _iokitReader.ReadingChanged -= OnConnectionStateChanged;
                _iokitReader.Dispose();
                _iokitReader = null;
            }
        }

        public override void Dispose()
        {
            _presenceCts?.Cancel();
            _presenceCts?.Dispose();
            _presenceCts = null;

            // StopHandling() already handles daemon removal, DeviceTree disposal,
            // and IOKit reader cleanup with proper null guards.
            StopHandling();

            WirelessKitDaemonBase.Ready -= OnDaemonReady;
            _instance = null;
            _daemon = null;

            _trayManager?.Dispose();
            _trayManager = null;

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}

#endif
