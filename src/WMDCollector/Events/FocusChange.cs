using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace WMDCollector
{
    public class FocusChange : Event
    {
        private SystemProcess process;

        public FocusChange(SystemProcess proc, long time)
            : base(time)
        {
            process = proc;
        }

        override public string ToString()
        {
            Dictionary<string, object> info = new Dictionary<string, object>();
            info.Add("_t", "eventlogger.FocusChange");
            info.Add("_id", this.Guid.ToString());
            info.Add("procId", this.process.StartEvent.Guid.ToString());
            info.Add("time", this.Time);
            string json = JsonConvert.SerializeObject(info, Formatting.None);
            return json;
        }
    }
}
