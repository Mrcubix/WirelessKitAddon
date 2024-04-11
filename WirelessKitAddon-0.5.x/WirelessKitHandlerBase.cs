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

        [SliderProperty("Early Warning Setting", -1, 100, 30),
         ToolTip("WirelessKitAddon: \n\n" +
                 "The battery level at which the user should be warned for the last time.\n" +
                 "-1 means that this warning is disabled.")]
        public float EarlyWarningSetting { get; set; }

        [SliderProperty("Late Warning Setting", -1, 100, 10),
         ToolTip("WirelessKitAddon: \n\n" +
                 "The battery level at which the user should be warned for the last time.\n" +
                 "-1 means that this warning is disabled.")]
        public float LateWarningSetting { get; set; }

        #endregion

        #region Methods

        public abstract void BringToDaemon();

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

            if (_trayManager.Start(_instance.Name) == false)
            {
                Log.Write("Wireless Kit Addon", "Failed to start the tray icon.", LogLevel.Error);
                return;
            }

            Log.Write("Wireless Kit Addon", "Tray icon started successfully.", LogLevel.Info);
        }

        #endregion

        #region Disposal

        public abstract void Dispose();

        #endregion
    }
}