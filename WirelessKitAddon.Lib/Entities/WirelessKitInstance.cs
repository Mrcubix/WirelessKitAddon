using CommunityToolkit.Mvvm.ComponentModel;

namespace WirelessKitAddon.Lib
{
    public partial class WirelessKitInstance : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private float _batteryLevel;

        [ObservableProperty]
        private bool _isCharging;

        public WirelessKitInstance(string name, 
                                   bool isConnected, 
                                   float batteryLevel, 
                                   bool isCharging = false,
                                   float earlyWarningSetting = 30, 
                                   float lateWarningSetting = 10,
                                   int timeBeforeNotification = 5)
        {
            Name = name;
            IsConnected = isConnected;
            BatteryLevel = batteryLevel;
            IsCharging = isCharging;
            EarlyWarningSetting = earlyWarningSetting;
            LateWarningSetting = lateWarningSetting;
            TimeBeforeNotification = timeBeforeNotification;
        }

        public float EarlyWarningSetting { get; }
        public float LateWarningSetting { get; }
        public int TimeBeforeNotification { get; }
    }
}