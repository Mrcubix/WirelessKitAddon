using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace WirelessKitAddon.Lib
{
    public interface IWirelessKitDaemon
    {
        public event EventHandler<WirelessKitInstance> InstanceAdded;
        public event EventHandler<WirelessKitInstance> InstanceRemoved;
        public event EventHandler<WirelessKitInstance> InstanceUpdated;

        /// <summary>
        ///   Get the instances of the wireless kit.
        /// </summary>
        public Task<IEnumerable<WirelessKitInstance>> GetInstances();

        /// <summary>
        ///   Get the instance with the given name.
        /// </summary>
        /// <param name="name">The name of the tablet associated with the instance.</param>
        public Task<WirelessKitInstance> GetInstance(string name);
    }
}