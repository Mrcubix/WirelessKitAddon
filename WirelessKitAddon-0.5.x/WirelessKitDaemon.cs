using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using WirelessKitAddon.Lib;
using WirelessKitAddon.Lib.RPC;

namespace WirelessKitAddon
{
    [PluginName("Wireless Kit Daemon")]
    public sealed class WirelessKitDaemon : WirelessKitDaemonBase, ITool
    {
        public WirelessKitDaemon()
        {
            _rpcServer ??= new RpcServer<WirelessKitDaemonBase>("WirelessKitDaemon", this);
            Instance ??= this;
        }

        #region Methods

        public override bool Initialize()
        {
            if (_rpcServer != null)
            {
                base.Initialize();
                _ = Task.Run(_rpcServer.MainAsync);
            }

            return true;
        }

        public void Dispose() => _rpcServer.Dispose();

        #endregion
    }
}