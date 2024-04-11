using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WirelessKitAddon.Lib.RPC;

namespace WirelessKitAddon.Lib
{
    public class WirelessKitDaemonBase : IWirelessKitDaemon
    {
        #region Events

        public virtual event EventHandler<WirelessKitInstance>? InstanceAdded;

        public virtual event EventHandler<WirelessKitInstance>? InstanceRemoved;

        public virtual event EventHandler<WirelessKitInstance>? InstanceUpdated;

#pragma warning disable CS0067

        public static event EventHandler? Ready;

#pragma warning restore CS0067

        #endregion

        #region Properties

        protected readonly List<WirelessKitInstance> _instances = new List<WirelessKitInstance>();
        
        protected static RpcServer<WirelessKitDaemonBase> _rpcServer = null!;

        public static WirelessKitDaemonBase? Instance { get; protected set; }

        #endregion

        #region Methods

        public virtual bool Initialize()
        {
            Ready?.Invoke(this, EventArgs.Empty);

            return true;
        }

        public bool Add(WirelessKitInstance instance)
        {
            if (_instances.Any(ins => ins.Name == instance.Name))
                return false;

            _instances.Add(instance);
            InstanceAdded?.Invoke(this, instance);

            return true;
        }

        public void Update(WirelessKitInstance instance)
            => InstanceUpdated?.Invoke(this, instance);

        public bool Remove(WirelessKitInstance instance)
        {
            if (_instances.Remove(instance))
            {
                InstanceRemoved?.Invoke(this, instance);
                return true;
            }

            return false;
        }

        public bool Remove(string name)
        {
            var instance = _instances.FirstOrDefault(ins => ins.Name == name);

            if (instance == null)
                return false;

            _instances.Remove(instance);
            InstanceRemoved?.Invoke(this, instance);

            return true;
        }

        public void Clear()
        {
            _instances.Clear();
        }

        public virtual Task<IEnumerable<WirelessKitInstance>> GetInstances()
        {
            return Task.FromResult(_instances.AsEnumerable());
        }

        public virtual Task<WirelessKitInstance> GetInstance(string name)
        {
            return Task.FromResult(_instances.FirstOrDefault(ins => ins.Name == name));
        }

        #endregion
    }
}