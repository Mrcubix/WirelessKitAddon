using System;
using System.Collections.Generic;
using System.Linq;

namespace WirelessKitAddon.Lib
{
    public class WirelessKitDaemon
    {
        public static event EventHandler? Ready;

        private readonly List<WirelessKitInstance> _instances = new List<WirelessKitInstance>();

        public bool Add(WirelessKitInstance instance)
        {
            if (_instances.Any(ins => ins.Name == instance.Name))
                return false;

            _instances.Add(instance);

            return true;
        }

        public bool Remove(WirelessKitInstance instance)
        {
            return _instances.Remove(instance);
        }

        public bool Remove(string name)
        {
            var instance = _instances.FirstOrDefault(ins => ins.Name == name);

            if (instance == null)
                return false;

            _instances.Remove(instance);

            return true;
        }

        public void Clear()
        {
            _instances.Clear();
        }
    }
}