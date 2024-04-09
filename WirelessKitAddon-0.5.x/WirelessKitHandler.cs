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

#if OTD05

namespace WirelessKitAddon
{
    [PluginName("Wireless Kit Addon")]
    public class WirelessKitHandler : IFilter
    {
        #region Constants

        private const ushort WACOM_VID = 1386;
        private const ushort WIRELESS_KIT_PID = 132;
        private const ushort WIRELESS_KIT_IDENTIFIER_INPUT_LENGTH = 32;
        private const ushort WIRELESS_KIT_IDENTIFIER_OUTPUT_LENGTH = 259;

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
            OutputInitReport = null,
            DeviceStrings = new(),
            InitializationStrings = new()
        };

        #endregion
        
        #region Fields

        private Driver? driver;
        private TabletState? tablet;
        private IOutputMode? outputMode;
        private DeviceReader<IDeviceReport>? reader;

        #endregion

        #region Initialization

        public WirelessKitHandler()
        {
            _ = Task.Run(LateInitializeAsync);
        }

        public async Task LateInitializeAsync()
        {
            await Task.Delay(15);

            driver = Info.Driver as Driver;
            tablet = driver?.Tablet;
            outputMode = driver?.OutputMode;

            if (driver == null || tablet == null || outputMode == null)
                return;

            // Tablet needs to be handled differently depending on whether it's in wireless mode or not
            if (tablet.Digitizer.VendorID == WACOM_VID && tablet.Digitizer.ProductID == WIRELESS_KIT_PID)
                HandleWirelessKit();
            // Any of the tablets identifiers PID matches the supported && vendorID is Wacom's vendorID
            else if (tablet.Digitizer.VendorID == WACOM_VID && SUPPORTED_TABLETS.Contains(tablet.Digitizer.ProductID))
                HandleWiredTablet();

            if (reader == null)
                Log.Write("Wireless Kit Addon", $"Failed to handle the Wireless Kit for {tablet.TabletProperties.Name}", LogLevel.Warning);
            else
                Log.Write("Wireless Kit Addon", $"Now handling Wireless Kit Reports for {tablet.TabletProperties.Name}", LogLevel.Info);
        }

        #endregion

        #region Methods

        private void HandleWirelessKit()
        {
            var matches = GetMatchingDevices(driver!, tablet!.TabletProperties, wirelessKitIdentifier);

            HandleMatch(driver!, matches);
        }

        private void HandleWiredTablet()
        {
            // We need to find the inputTree with an identifier of input length 10
            var identifier = tablet!.TabletProperties.DigitizerIdentifiers.FirstOrDefault(identifier => identifier.InputReportLength == 10 ||
                                                                                                       identifier.InputReportLength == 11);

            if (identifier == null)
                return;

            // we need to create a copy of the device and change the parser to our wireless parser
            var matches = GetMatchingDevices(driver!, tablet.TabletProperties, identifier);

            HandleMatch(driver!, matches);
        }

        private bool HandleMatch(Driver driver, IEnumerable<HidDevice> matches)
        {
            if (matches.Count() > 1)
                Log.Write("Wireless Kit Addon", "Multiple devices matched the Wireless Kit identifier. This is unexpected.", LogLevel.Warning);

            foreach (var match in matches)
            {
                if (match == null)
                    continue;

                try
                {
                    reader = new DeviceReader<IDeviceReport>(match, new WirelessReportParser());
                    reader.Report += (_, report) => driver.HandleReport(report);
                }
                catch (Exception ex)
                {
                    Log.Write("Wireless Kit Addon", $"Failed to create a reader for the Wireless Kit: \n{ex.Message}", LogLevel.Error);
                    continue;
                }
            }

            return reader != null;
        }

        #endregion

        #region Properties

        public FilterStage FilterStage => FilterStage.PreTranspose;

        #endregion

        #region Methods

        public Vector2 Filter(Vector2 point) => point;

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
    }
}

#endif