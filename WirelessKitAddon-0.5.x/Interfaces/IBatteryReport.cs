using OpenTabletDriver.Plugin.Tablet;

namespace WirelessKitAddon.Interfaces
{
    public interface IBatteryReport : IDeviceReport
    {
        float Battery { get; set; }
        bool IsCharging { get; set; }
    }
}