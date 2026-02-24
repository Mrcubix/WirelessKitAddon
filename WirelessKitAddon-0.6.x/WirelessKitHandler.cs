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

        private TabletReference? _tablet;

        private InputDevice? _device;

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
                // where the dongle only exposes 0-byte, 10-byte, and 64-byte interfaces),
                // set up the daemon and tray icon anyway. Battery level will show as "unknown"
                // since macOS does not expose the wireless status HID interface.
                if (DeviceTree == null && _driver.CompositeDeviceHub.GetDevices()
                    .Any(d => d.VendorID == WACOM_VID && d.ProductID == WIRELESS_KIT_PID))
                {
                    _isWireless = true;
                    Log.Write("Wireless Kit Addon",
                        "Using wireless passthrough mode — battery status is unavailable on this platform.",
                        LogLevel.Info);
                }

                if (DeviceTree == null && !_isWireless)
                    Log.Write("Wireless Kit Addon", $"Failed to handle the Wireless Kit for {tablet.Properties.Name}", LogLevel.Warning);
                else
                {
                    BringToDaemon();

                    if (_daemon != null && _instance != null)
                    {
                        // In passthrough mode, battery data is not available — set to -1
                        // which triggers the "battery_unknown" icon in the tray UI.
                        if (_isWireless && DeviceTree == null)
                            _instance.BatteryLevel = -1;

                        _ = Task.Run(SetupTrayIcon);

                        Log.Write("Wireless Kit Addon", $"Now handling Wireless Kit Reports for {_instance.Name}", LogLevel.Info);
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

            // On macOS, the 32-byte endpoint does not exist. The dongle only exposes:
            //   - InputLen=0  (control, FeatureLen=259)
            //   - InputLen=10 (pen data, shared with OTD's main reader)
            //   - InputLen=64 (aux/express keys)
            // None of these carry battery status reports (0xC0/0x80).
            // Battery reading is not possible — we fall through to passthrough mode.
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
                }

                if (report is IWirelessKitReport wirelessReport)
                    _instance.IsConnected = wirelessReport.IsConnected;
                else
                    _instance.IsConnected = true;

                _daemon?.Update(_instance);
            }

            Emit?.Invoke(report);
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
        }

        public override void Dispose()
        {
            if (_instance != null && _daemon != null)
                StopHandling();

            if (DeviceTree != null)
            {
                foreach (var device in DeviceTree.InputDevices)
                    device.Dispose();

                DeviceTree = null;
            }

            WirelessKitDaemonBase.Ready -= OnDaemonReady;
            _instance = null;
            _daemon = null;

            _trayManager?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}

#endif
