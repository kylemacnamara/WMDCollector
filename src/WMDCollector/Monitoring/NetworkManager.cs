using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Diagnostics;
using System.Net.NetworkInformation;
namespace WMDCollector
{
    /// <summary>
    /// The Network manager tracks local IP addresses and also returns which ports are being listened to.
    /// </summary>
    class NetworkManager
    {
        private HashSet<IPAddress> localAddresses;
        public long LastFlushedConnections { get; set; }

        public NetworkManager()
        {
            LastFlushedConnections = Utilities.GetCurrentTime();
            localAddresses = GetLocalIPs();
        }

        public HashSet<IPAddress> GetLocalIPs()
        {
            HashSet<IPAddress> locals = new HashSet<IPAddress>();
        
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        locals.Add(ip.Address);
                    }
                }
            }
            
            return locals;
        }
        public void LogSourceAddress(IPAddress addr, String protocol)
        {
           // The source address for TCP seems to always be local..
            if (protocol == Protocol.TCP)
            {
                if (!localAddresses.Contains(addr))
                {
                    localAddresses = GetLocalIPs();
                }
            }
        }

        public bool IsLocal(IPAddress addr)
        {
            return localAddresses.Contains(addr);
        }

        public IPEndPoint[] GetListeners(String protocol)
        {
            if (protocol == Protocol.TCP)
            {
               return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            }
            else
            {
                return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();
            }
        }
    }
}
