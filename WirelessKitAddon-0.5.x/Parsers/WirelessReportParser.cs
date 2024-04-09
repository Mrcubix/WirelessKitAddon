using WirelessKitAddon.Reports;
using OpenTabletDriver.Plugin.Tablet;

namespace WirelessKitAddon.Parsers
{
    public class WirelessReportParser : IReportParser<IDeviceReport>
    {
        private const byte WACOM_REPORT_WL = 128;
        private const byte WACOM_REPORT_USB = 192;

        public IDeviceReport Parse(byte[] report)
        {
            if (report.Length == 32 && report[0] == WACOM_REPORT_WL)
                return new Wacom32bWirelessReport(report);

            // A different, but similar report is used when wired
            if (report.Length == 10 && report[0] == WACOM_REPORT_USB)
                return new Wacom10bWirelessReport(report);
            
            return new DeviceReport(report);
        }
    }
}