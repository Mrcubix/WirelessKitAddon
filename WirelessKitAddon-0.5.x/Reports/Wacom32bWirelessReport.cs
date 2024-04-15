using WirelessKitAddon.Interfaces;

namespace WirelessKitAddon.Reports
{
    /// <summary>
    ///   Structure of a wireless report: <br />
    ///   report[0]: Report ID <br />
    ///   report[1]: Connection status <br />
    ///   report[2..4]: Unknown <br />
    ///   report[5]: Battery status <br />
    ///   report[6..31]: Unknown <br />
    /// </summary>
    public class Wacom32bWirelessReport : IWirelessKitReport
    {
        public Wacom32bWirelessReport(byte[] report)
        {
            Raw = report;

            IsConnected = (report[1] & 0x01) != 0;

            Battery = (report[5] & 0x3F) * 100 / 31f;
            IsCharging = (report[5] & 0x80) != 0;
        }

        public byte[] Raw { get; set; }

        public bool IsConnected { get; set; }

        public float Battery { get; set; }

        public bool IsCharging { get; set; }
    }
}