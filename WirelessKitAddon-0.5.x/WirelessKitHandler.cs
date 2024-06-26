using System.Numerics;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using System.Collections.Immutable;
using WirelessKitAddon.Parsers;
using System.Collections.Generic;
using HidSharp;
using System.Linq;
using System;
using OpenTabletDriver.Devices;
using WirelessKitAddon.Lib;
using WirelessKitAddon.Interfaces;

#if OTD05

namespace WirelessKitAddon
{
    [PluginName("Wireless Kit Addon")]
    public class WirelessKitHandler : WirelessKitHandlerBase, IFilter, IDisposable
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
            209, 210, 211, 214, 215, 219, 222, 223, 770, 771, 828, 830
        );

        private readonly DeviceIdentifier wirelessKitIdentifier = new()
        {
            VendorID = WACOM_VID,
            ProductID = WIRELESS_KIT_PID,
            InputReportLength = WIRELESS_KIT_IDENTIFIER_INPUT_LENGTH,
            OutputReportLength = 0,
            ReportParser = typeof(WirelessReportParser).FullName ?? string.Empty,
            FeatureInitReport = new byte[2] { 0x02, 0x02 },
            OutputInitReport = null!,
            DeviceStrings = new(),
            InitializationStrings = new()
        };

        #endregion

        #region Fields

        private readonly byte[] _powerSavingReport = new byte[13] { 0x03, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 };
        private TabletState? _tablet;
        private IOutputMode? _outputMode;
        private DeviceReader<IDeviceReport>? _reader;

        #endregion

        #region Initialization

        public WirelessKitHandler() : base()
        {
            WirelessKitDaemonBase.Ready += OnDaemonReady;

            _ = Task.Run(LateInitializeAsync);
        }

        public async Task LateInitializeAsync()
        {
            await Task.Delay(30);
            //Initialize();
        }

        public void Initialize()
        {
            _driver = Info.Driver as Driver;
            _tablet = _driver?.Tablet;
            _outputMode = _driver?.OutputMode;

            if (_driver == null || _tablet == null || _outputMode == null || _daemon == null)
                return;

            // Tablet needs to be handled differently depending on whether it's in wireless mode or not
            if (_tablet.Digitizer.VendorID == WACOM_VID && _tablet.Digitizer.ProductID == WIRELESS_KIT_PID)
                HandleWirelessKit((_driver as Driver)!);
            // Any of the tablets identifiers PID matches the supported && vendorID is Wacom's vendorID
            else if (_tablet.Digitizer.VendorID == WACOM_VID && SUPPORTED_TABLETS.Contains(_tablet.Digitizer.ProductID))
                HandleWiredTablet((_driver as Driver)!);

            if (_reader == null)
                Log.Write("Wireless Kit Addon", $"Failed to handle the Wireless Kit for {_tablet.TabletProperties.Name}", LogLevel.Warning);
            else
            {
                BringToDaemon();

                if (_daemon != null && _instance != null)
                {
                    WirelessKitDaemonBase.Ready -= OnDaemonReady;
                    _ = Task.Run(SetupTrayIcon);

                    Log.Write("Wireless Kit Addon", $"Now handling Wireless Kit Reports for {_instance.Name}", LogLevel.Info);
                }
            }
        }

        protected override void HandleWirelessKit(Driver driver)
        {
            var matches = GetMatchingDevices(driver, _tablet!.TabletProperties, wirelessKitIdentifier);

            HandleMatch(matches);
        }

        protected override void HandleWiredTablet(Driver driver)
        {
            // We need to find the inputTree with an identifier of input length 10
            var identifier = _tablet!.TabletProperties.DigitizerIdentifiers.FirstOrDefault(identifier => identifier.InputReportLength == 10 ||
                                                                                                         identifier.InputReportLength == 11);

            if (identifier == null)
                return;

            // we need to create a copy of the device and change the parser to our wireless parser
            var matches = GetMatchingDevices(driver, _tablet.TabletProperties, identifier);

            HandleMatch(matches);
        }

        private bool HandleMatch(IEnumerable<HidDevice> matches)
        {
            if (matches.Count() > 1)
                Log.Write("Wireless Kit Addon", "Multiple devices matched the Wireless Kit identifier. This is unexpected.", LogLevel.Warning);

            foreach (var match in matches)
            {
                if (match == null)
                    continue;

                try
                {
                    _reader = new DeviceReader<IDeviceReport>(match, new WirelessReportParser());
                    _reader.Report += HandleReport;
                    _reader.ReadingChanged += OnConnectionStateChanged;
                }
                catch (Exception ex)
                {
                    Log.Write("Wireless Kit Addon", $"Failed to create a reader for the Wireless Kit: \n{ex.Message}", LogLevel.Error);
                    continue;
                }
            }

            SetBatterySavingModeTimeout();

            return _reader != null;
        }

        #endregion

        #region Properties

        public FilterStage FilterStage => FilterStage.PreTranspose;

        #endregion

        #region Methods

        public Vector2 Filter(Vector2 point) => point;

        public override void BringToDaemon()
        {
            if (_daemon == null)
                return;

            _instance = new WirelessKitInstance(_tablet!.TabletProperties.Name, false, 0, false, EarlyWarningSetting, LateWarningSetting, NotificationTimeout);

            _daemon.Add(_instance);
        }

        public override void SetBatterySavingModeTimeout()
        {
            if (_reader == null)
                return;

            _powerSavingReport[2] = Math.Clamp((byte)PowerSavingTimeout, POWER_SAVING_MIN_TIMEOUT, POWER_SAVING_MAX_TIMEOUT);

            try
            {
                _reader.ReportStream.SetFeature(_powerSavingReport);
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
                Initialize();
        }

        private void OnConnectionStateChanged(object? sender, bool connected)
        {
            if (connected == false) // Pre-Dispose on disconnect
                StopHandling();
        }

        public void HandleReport(object? sender, IDeviceReport report)
        {
            if (_instance == null)
                return;

            if (report is IBatteryReport batteryReport)
            {
                _instance.BatteryLevel = batteryReport.Battery;
                _instance.IsCharging = batteryReport.IsCharging;
            }

            if (report is IWirelessKitReport wirelessReport)
                _instance.IsConnected = wirelessReport.IsConnected;

            _daemon?.Update(_instance);
        }

        #endregion

        #region Static Methods

        private IEnumerable<HidDevice> GetMatchingDevices(Driver driver, TabletConfiguration configuration, DeviceIdentifier identifier)
        {
            return from device in DeviceList.Local.GetHidDevices()
                   where identifier.VendorID == device.VendorID
                   where identifier.ProductID == device.ProductID
                   where TryDeviceOpen(device)
                   where identifier.InputReportLength == null || identifier.InputReportLength == device.GetMaxInputReportLength()
                   where identifier.OutputReportLength == null || identifier.OutputReportLength == device.GetMaxOutputReportLength()
                   select device;
        }

        private static bool TryDeviceOpen(HidDevice device)
        {
            try
            {
                return device.CanOpen;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                return false;
            }
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

            // Dispose of the reader, 
            // No need to dispose of the output mode as it's not owned by us
            if (_reader != null)
            {
                _reader.Report -= HandleReport;
                _reader.ReadingChanged -= OnConnectionStateChanged;
                _reader.Dispose();
                _reader = null;
            }
        }

        public override void Dispose()
        {
            if (_instance != null && _daemon != null)
               StopHandling();

            if (_reader != null)
            {
                _reader.Report -= HandleReport;
                _reader.Dispose();
                _reader = null;
            }

            WirelessKitDaemonBase.Ready -= OnDaemonReady;
            _daemon = null;

            _trayManager?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}

#endif