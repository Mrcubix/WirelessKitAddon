using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HidSharp;
using OpenTabletDriver;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;

namespace WirelessKitAddon.Lib
{
    public abstract class WirelessKitHandlerBase : IDisposable
    {
        #region Fields

        protected IDriver? _driver;
        
        protected WirelessKitDaemonBase? _daemon;
        protected WirelessKitInstance? _instance;
        protected TrayManager? _trayManager;

        #endregion

        #region Initialization

        protected abstract void HandleWirelessKit(Driver driver);

        protected abstract void HandleWiredTablet(Driver driver);

        #endregion

        #region Properties

        [SliderProperty("Early Low Battery Notification Threshold", -1f, 100f, 30f),
         DefaultPropertyValue(30f),
         Unit("%"),
         ToolTip("WirelessKitAddon: \n\n" +
                 "The battery level at which the user should be warned for the last time.\n" +
                 "-1 means that this warning is disabled.")]
        public float EarlyWarningSetting { get; set; }

        [SliderProperty("Late Low Battery Notification Threshold", -1f, 100f, 10f),
         DefaultPropertyValue(10f),
         Unit("%"),
         ToolTip("WirelessKitAddon: \n\n" +
                 "The battery level at which the user should be warned for the last time.\n" +
                 "-1 means that this warning is disabled.")]
        public float LateWarningSetting { get; set; }

        [Property("Switch to power saving mode after"),
         DefaultPropertyValue(2),
         Unit("minutes"),
         ToolTip("WirelessKitAddon: \n\n" +
                 "The time after which the tablet should switch to power saving mode. \n" +
                 "Minimum value: 1 minute, Maximum value: 20 minutes.")]
        public int PowerSavingTimeout { get; set; }

        [Property("Time before sending notifications"),
         DefaultPropertyValue(5),
         Unit("seconds"),
         ToolTip("WirelessKitAddon: \n\n" +
                 "The time before sending notifications when a tablet is connected.\n\n" +
                 "A value below 0 means that notifications are disabled.")]
        public int NotificationTimeout { get; set; }

        #endregion

        #region Methods

        public abstract void BringToDaemon();

        public abstract void SetBatterySavingModeTimeout();

        public async Task SetupTrayIcon()
        {
            if (_instance == null)
                return;

            _trayManager = new TrayManager();

            if (await _trayManager.Setup() == false)
            {
                Log.Write("Wireless Kit Addon", "Failed to setup the tray icon.", LogLevel.Error);
                return;
            }

            _trayManager.Start(_instance.Name);
        }

        #endregion

        #region Disposal

        public abstract void Dispose();

        #endregion
    }
}