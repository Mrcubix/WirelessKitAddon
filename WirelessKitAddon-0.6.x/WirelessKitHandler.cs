using OpenTabletDriver;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.DependencyInjection;
using WirelessKitAddon.Parsers;
using OpenTabletDriver.Plugin.Devices;
using System.Collections.Immutable;

#if OTD06

namespace WirelessKitAddon
{
    [PluginName("Wireless Kit Addon")]
    public class WirelessKitHandler : IPositionedPipelineElement<IDeviceReport>, IDisposable
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
            FeatureInitReport = new List<byte[]> { new byte[2] { 0x02, 0x02 } },
            OutputInitReport = null,
            DeviceStrings = new(),
            InitializationStrings = new()
        };

        #endregion

        #region Fields

        private IDriver? _driver;
        private TabletReference? tablet;

        #endregion

        #region Initialization

        public void Initialize(IDriver? driver, TabletReference? tablet)
        {
            if (driver is Driver _driver && tablet != null)
            {
                // Need to fetch the tree first to obtain the output mode
                var tabletTree = _driver.InputDevices.FirstOrDefault(tree => tree.Properties == tablet.Properties);
                OutputMode = tabletTree?.OutputMode;

                if (OutputMode == null)
                    return;

                // Tablet needs to be handled differently depending on whether it's in wireless mode or not
                if (tablet.Identifiers.Any(identifier => identifier.VendorID == WACOM_VID &&
                                                         identifier.ProductID == WIRELESS_KIT_PID))
                    HandleWirelessKit(_driver);
                // Any of the tablets identifiers PID matches the supported && vendorID is Wacom's vendorID
                else if (tablet.Identifiers.Any(identifier => identifier.VendorID == WACOM_VID &&
                                                              SUPPORTED_TABLETS.Contains(identifier.ProductID)))
                    HandleWiredTablet(_driver);

                if (DeviceTree != null)
                    Log.Write("Wireless Kit Addon", $"Now handling Wireless Kit Reports for {tablet.Properties.Name}", LogLevel.Info);
                else
                    Log.Write("Wireless Kit Addon", $"Failed to handle the Wireless Kit for {tablet.Properties.Name}", LogLevel.Warning);
            }
        }

        private void HandleWirelessKit(Driver driver)
        {
            var matches = GetMatchingDevices(driver, Tablet!.Properties, wirelessKitIdentifier);

            HandleMatch(driver, matches);
        }

        private void HandleWiredTablet(Driver driver)
        {
            // We need to find the inputTree with an identifier of input length 10
            var tree = driver.InputDevices.FirstOrDefault(tree => tree.Properties == Tablet!.Properties);
            var device = tree?.InputDevices.FirstOrDefault(device => device.Identifier.InputReportLength == 10 || 
                                                                     device.Identifier.InputReportLength == 11);

            if (device == null)
                return;

            // we need to create a copy of the device and change the parser to our wireless parser
            var matches = GetMatchingDevices(driver, Tablet!.Properties, device.Identifier);
            driver.CompositeDeviceHub.GetDevices().Select(dev => dev.VendorID == device.Identifier.VendorID &&
                                                                 dev.ProductID == device.Identifier.ProductID &&
                                                                 dev.InputReportLength == device.Identifier.InputReportLength &&
                                                                 dev.OutputReportLength == device.Identifier.OutputReportLength);

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

                InputDevice device;

                try
                {
                    device = new InputDevice(driver, match, Tablet!.Properties, wirelessKitIdentifier);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, LogLevel.Warning);
                    continue;
                }

                DeviceTree = new InputDeviceTree(Tablet.Properties, new List<InputDevice> { device })
                {
                    OutputMode = OutputMode
                };
            }

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
            set
            {
                _driver = value;

                if (value != null)
                    Initialize(value, Tablet);
            }
        }

        [TabletReference]
        public TabletReference? Tablet
        {
            get => tablet;
            set
            {
                tablet = value;

                if (Driver != null)
                    Initialize(Driver, value);
            }
        }

        public PipelinePosition Position => PipelinePosition.None;

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

        public void Consume(IDeviceReport value) => Emit?.Invoke(value);

        #endregion

        #region Static Methods

        private IEnumerable<IDeviceEndpoint> GetMatchingDevices(Driver driver, TabletConfiguration configuration, DeviceIdentifier identifier)
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

        public void Dispose()
        {
            
        }

        #endregion
    }
}

#endif