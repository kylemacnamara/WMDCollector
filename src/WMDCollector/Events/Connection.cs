using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WMDCollector
{

    public class ConnectionMetadata
    {
        public IPEndPoint Local { get; private set; }

        public IPEndPoint Remote { get; private set; }

        // Either "TCP" or "UDP"
        public string Proto { get; private set; }

        public ConnectionMetadata(IPEndPoint local, IPEndPoint remote, String protocol) 
        {
            Local = local;
            Remote = remote;
            Proto = protocol;
        }
        public override bool Equals(Object obj)
        {
            // Check for null values and compare run-time types.
            if (obj == null || GetType() != obj.GetType())
                return false;

            ConnectionMetadata data = (ConnectionMetadata)obj;
            return (this.Local.Equals(data.Local)) &&
                (this.Remote.Equals(data.Remote)) &&
                (this.Proto.Equals(data.Proto));
        }
        public override int GetHashCode()
        {
            unchecked // Overflow is fine, just wrap
            {
                int hash = 17;
                hash = hash * 23 + Local.GetHashCode();
                hash = hash * 23 + Remote.GetHashCode();
                hash = hash * 23 + Proto.GetHashCode();
                return hash;
            }
        }
    }

    public sealed class Protocol
    {
        public static readonly string TCP = "TCP";
        public static readonly string UDP = "UDP";
    }

    public class ConnectionUpdate : Event
    {
        private Connection connection;
        public ConnectionUpdate(Connection conn)
            : base(conn.Time)
        {
            connection = conn;
            // Must have the same guids!
            this.Guid = conn.Guid;
        }
        override public string ToString()
        {
            Dictionary<string, object> info = new Dictionary<string, object>();
            info.Add("_t", "ConnectionUpdate");
            info.Add("_id", connection.Guid.ToString());
            info.Add("procId", connection.Process.StartEvent.Guid.ToString());
            info.Add("localIp", connection.Local.Address.ToString());
            info.Add("localPort", connection.Local.Port);
            info.Add("remoteIp", connection.Remote.Address.ToString());
            info.Add("remotePort", connection.Remote.Port);
            info.Add("protocol", connection.Proto);

            if (connection.InBound)
            {
                info.Add("inbound", connection.InBound);
            }
            info.Add("bytesRecv", connection.bytesReceived);
            info.Add("bytesSent", connection.bytesSent);
            info.Add("time", connection.Time);
            info.Add("timeEnd", connection.EndTime);
            string json = JsonConvert.SerializeObject(info, Formatting.None);
            return json;
        }

    }

    public class Connection : Event
    {

        private ConnectionMetadata metadata;
        public IPEndPoint Local
        {
            get
            {
                return metadata.Local;
            }
        }

        public IPEndPoint Remote
        {
            get
            {
                return metadata.Remote;
            }
        }

        public string Proto
        {
            get
            {
                return metadata.Proto;
            }
        }

        public Boolean InBound { get; private set; }

        public int bytesSent { get; private set; }

        public int bytesReceived { get; private set; }

        public SystemProcess Process { get; private set; }

        public long EndTime { get; private set; }

        // Does the server have the most recent information regarding this connection
        private bool flushed;

        public Connection(SystemProcess proc, string prot, IPEndPoint local, IPEndPoint remote, bool inbound, long time)
            : base(time)
        {
            Process = proc;
            metadata = new ConnectionMetadata(local, remote, prot);
            InBound = inbound;
            bytesReceived = 0;
            bytesSent = 0;
            flushed = true;
        }

        /**
         * Produces a ConnectionUpdate event if the following conditions are met:
         *      1) The connection has not yet been flushed. In other words, we have received
         *          or sent data since the last time we told the server. 
         *      2) It has been 20 seconds since we last sent/received any data on this connection. 
         */
        public ConnectionUpdate ReceiveUpdate(long currentTime)
        {
            if (this.Proto == Protocol.UDP)
            {
                int timeElapsed = Utilities.ElapsedTime(this.EndTime, currentTime);
                if (this.flushed == false && (timeElapsed >= 20))
                {
                    this.flushed = true;
                    return new ConnectionUpdate(this);
                }
                else return null;
            }
            else
            {
                return new ConnectionUpdate(this);
            }
        }

        public void SentAdditionalBytes(int numBytes, long timeSent)
        {
            if (numBytes > 0)
            {
                this.flushed = false;
                this.EndTime = timeSent;
                this.bytesSent += numBytes;
            }
        }

        public void ReceivedAdditionalBytes(int numBytes, long timeReceived)
        {
            if (numBytes > 0)
            {
                this.flushed = false;
                this.EndTime = timeReceived;
                this.bytesReceived += numBytes;
            }
        }

      
        override public string ToString()
        {
            Dictionary<string, object> info = new Dictionary<string, object>();
            info.Add("_t", "eventlogger.Connection");
            info.Add("_id", this.Guid.ToString());
            info.Add("procId", this.Process.StartEvent.Guid.ToString());
            info.Add("localIp", this.Local.Address.ToString());
            info.Add("localPort", this.Local.Port);
            info.Add("remoteIp", this.Remote.Address.ToString());
            info.Add("remotePort", this.Remote.Port);
            info.Add("protocol", this.Proto);
            if (this.InBound)
            {
                info.Add("inbound", this.InBound);
            }
            info.Add("time", this.Time);
            string json = JsonConvert.SerializeObject(info, Formatting.None);
            return json;
        }
    }
}
