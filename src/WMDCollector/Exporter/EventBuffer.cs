using System;

using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.IO;
using MongoDB.Bson;

namespace WMDCollector
{
    class EventBuffer
    {
        // Number of seconds to wait since last post before posting again
        const int TIMEOUT = 30;
        
        private HttpCollector collector;
        private ConcurrentDictionary<String, Event> buffer; 

        // Events that do not need any processing. They can be sent immediately rather than being processed by a thread pool.
        private readonly HashSet<String> quickEvents = new HashSet<string>{"ProcessEnd", "Connection", "ConnectionUpdate", "HeartBeat", "FocusChange"};

        private Timer postTimer;

        public EventBuffer()
        {
            collector = new HttpCollector();
            buffer = new ConcurrentDictionary<String, Event>(); 
            postTimer = new Timer(CheckCapacity, null, 0, 30 * 1000);
        }
  
        private void AddEventToBuffer(Event e, String eventName)
        {
            bool success = buffer.TryAdd(e.Guid + eventName, e);
        }

        public void AddEvent(Event e)
        {
            String eventName = e.GetType().Name;
            if (this.quickEvents.Contains(eventName))
            {
                AddEventToBuffer(e, eventName);
            }
            else
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    e.FinishProcessing();
                    AddEventToBuffer(e, eventName);
                });
            }
        }

        private string BuildPayload()
        {
            IEnumerable<string> strings = buffer.Select(i => i.ToString());
            string events = String.Join(",", strings.ToArray());
            return String.Format(@"[{0}]", events);
        }

        private void CheckCapacity(object sender)
        {
            List<Event> currentBatch = new List<Event>();
            if (!buffer.IsEmpty)
            {
                Console.WriteLine("Posting {0} events", buffer.Count);
                var enumerator = buffer.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var pair = enumerator.Current;
                    currentBatch.Add(pair.Value);
                    Event removedEvent;
                    buffer.TryRemove(pair.Key, out removedEvent);
                }
                IEnumerable<string> strings = currentBatch.Select(i => i.ToString());
                string events = String.Join(",", strings.ToArray());
                collector.Post(String.Format(@"[{0}]", events));
            }
        }
    }
}
