using OpenTabletDriver.Plugin.Tablet;

namespace WirelessKitAddon.Reports
{
    internal struct DeviceReport : IDeviceReport
    {
        internal DeviceReport(byte[] report)
        {
            Raw = report;
        }

        public byte[] Raw { set; get; }
    }
}