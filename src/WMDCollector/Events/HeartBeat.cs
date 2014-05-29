using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace WMDCollector
{
    public class HeartBeat : Event
    {
        public HeartBeat(long time)
            : base(time)
        {
        }
        override public string ToString()
        {
            Dictionary<string, object> info = new Dictionary<string, object>();
            info.Add("_t", "eventlogger.HeartBeat");
            info.Add("_id", this.Guid.ToString());
            info.Add("time", this.Time);
            string json = JsonConvert.SerializeObject(info, Formatting.None);
            return json;
        }
    }
}
