using CommunityToolkit.Mvvm.ComponentModel;

namespace WirelessKitAddon.Lib
{
    public partial class WirelessKitInstance : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private float _batteryLevel;

        [ObservableProperty]
        private bool _isCharging;

        public WirelessKitInstance(string name, float batteryLevel, bool isCharging = false, float earlyWarningSetting = 30, float lateWarningSetting = 10)
        {
            Name = name;
            BatteryLevel = batteryLevel;
            IsCharging = isCharging;
            EarlyWarningSetting = earlyWarningSetting;
            LateWarningSetting = lateWarningSetting;
        }

        public float EarlyWarningSetting { get; }
        public float LateWarningSetting { get; }
    }
}