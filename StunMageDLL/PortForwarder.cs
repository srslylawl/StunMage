

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Mono.Nat;

namespace STUN
{
    public class PortForwarder
    {
        private INatDevice _natDevice;

        public PortForwarder()
        {
            NatUtility.DeviceFound += NatUtility_DeviceFound;
            NatUtility.DeviceLost += NatUtility_DeviceLost;
        }

        private void NatUtility_DeviceFound(object sender, DeviceEventArgs e)
        {
            _natDevice = e.Device;
        }

        private void NatUtility_DeviceLost(object sender, DeviceEventArgs e)
        {
            _natDevice = null;
        }

        public string DiscoverPublicIpAddress()
        {
            IPAddress address = _natDevice.GetExternalIP();
            return address.ToString();
        }

        public async Task<bool> DiscoverDevicesAsync(Action<string> logFunc = null)
        {
            NatUtility.StartDiscovery();

            int msWaited = 0;

            // We don't want to check more than 10 seconds.
            const int maxTime = 10000;
            while (_natDevice == null && msWaited < maxTime)
            {
                msWaited += 500;
                await Task.Delay(500);
                logFunc?.Invoke($"Waiting for NAT Device.. (time remaining: {(maxTime-msWaited)/1000.0})");
            }

            return _natDevice != null;
        }

        public bool DiscoverDevices(int maxTimeMS = 10000, Action<string> logFunc = null)
        {
            NatUtility.StartDiscovery();

            int msWaited = 0;

            // We don't want to check more than 10 seconds.
            while (_natDevice == null && msWaited < maxTimeMS)
            {
                msWaited += 500;
                Thread.Sleep(500);
                logFunc?.Invoke($"Waiting for NAT Device.. (time remaining: {(maxTimeMS-msWaited)/1000.0})");
            }

            return _natDevice != null;
        }

        public bool ForwardPort(MappingProtocol protocol, int port, Action<string> logFunc = null)
        {
            List<Mapping> mappingsToForward = new List<Mapping>(2);

            if (protocol.HasFlag(MappingProtocol.Tcp))
                mappingsToForward.Add(new Mapping(Protocol.Tcp, port, port));

            if (protocol.HasFlag(MappingProtocol.Udp))
                mappingsToForward.Add(new Mapping(Protocol.Udp, port, port));

            try
            {
                foreach (Mapping mapping in mappingsToForward)
                    _natDevice.CreatePortMap(mapping);

                return true;
            }
            catch (Exception ex)
            {
                logFunc?.Invoke($"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }
    }
}
