using WirelessKitAddon.Interfaces;

namespace WirelessKitAddon.Reports
{
    /// <summary>
    ///   Structure of a wireless report: <br />
    ///   report[0]: Report ID <br />
    ///   report[1..7]: Unknown <br />
    ///   report[8]: Battery status <br />
    ///   report[9]: Wireless Kit attached <br />
    /// </summary>
    public class Wacom10bWirelessReport : IBatteryReport
    {
        public Wacom10bWirelessReport(byte[] report)
        {
            Raw = report;

            Battery = (report[8] & 0x3F) * 100 / 31f;
            IsCharging = (report[8] & 0x80) != 0;
        }

        public byte[] Raw { get; set; }

        public float Battery { get; set; }

        public bool IsCharging { get; set; }
    }
}