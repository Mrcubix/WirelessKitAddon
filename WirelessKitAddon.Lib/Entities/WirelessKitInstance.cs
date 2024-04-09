using CommunityToolkit.Mvvm.ComponentModel;

namespace WirelessKitAddon.Lib
{
    public class WirelessKitInstance : ObservableObject
    {
        public WirelessKitInstance(string name, float batteryLevel, bool isCharging)
        {
            Name = name;
            BatteryLevel = batteryLevel;
            IsCharging = isCharging;
        }

        public string Name { get; }

        public float BatteryLevel { get; }

        public bool IsCharging { get; }
    }
}