using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Threading;
using MongoDB.Bson;

namespace WMDCollector
{
    public abstract class Event : IComparable<Event>
    {
        /// <summary>
        /// The number of ticks that represent the date and time of this instance.
        /// A single tick represents one hundred nanoseconds or one ten-millionth of a second. 
        /// There are 10,000 ticks in a millisecond.
        /// 
        /// The value of this property represents the number of 100-nanosecond intervals that 
        /// have elapsed since the year 1601. 
        /// </summary>
        public long Time;
        public ObjectId Guid;

        // 
        public Event(long time)
        {
            Time = time;
            // Create a MongoDB ObjectID with the correct timestamp
            var unixTime = ConvertToUnixTime(time);
            var unixTimeHex = unixTime.ToString("X8").ToLower();
            // 4-byte value representing the seconds since the Unix epoch
            var guidString = ObjectId.GenerateNewId().ToString();
            var newGuid = unixTimeHex + guidString.Substring(8);
            Guid = new ObjectId(newGuid);
        }

        private long ConvertToUnixTime(long time)
        {
            time = time - 116444736000000000;
            return time / (10000 * 1000L);
        }

        // By default, no processing is required
        public virtual void FinishProcessing() { }

        public int CompareTo(Event other)
        {
            return this.Time.CompareTo(other.Time);
        }
    }
}

