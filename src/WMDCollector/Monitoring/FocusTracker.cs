using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Newtonsoft.Json;
using System.Windows.Automation;

namespace WMDCollector
{
    [EventSource(Name = "EventLogger-Focus")]
    public sealed class Logger : EventSource
    {

        [Event(60)]
        public void FocusChange(int PID) { WriteEvent(60, PID); }
        public static Logger Log = new Logger();
    }

    /// <summary>
    /// Tracks focus changes and records the pid of the process responsible and the timestamp by generating an event.
    /// This event is listened to by the Monitor by subscribing to a dynamic provider. 
    /// </summary>
    public class FocusTracker
    {
        private int lastProc;
        private object intLock = new object();

        public FocusTracker()
        {
            lock (intLock)
            {
                lastProc = 0;
            }
            Process parentProc = ParentProcessUtilities.GetParentProcess();
            if (parentProc != null)
            {
                parentProc.EnableRaisingEvents = true;
                parentProc.Exited += delegate(object sender, EventArgs e)
                {
                    Logger.Log.Dispose();
                    System.Windows.Forms.Application.Exit();
                };
            }

        }
        public void TrackFocus()
        {
            Automation.AddAutomationFocusChangedEventHandler(OnFocusChangedHandler);
            Application.Run(new NoGUI());
        }
        private void OnFocusChangedHandler(object src, AutomationFocusChangedEventArgs args)
        {
            try
            {
                AutomationElement element = src as AutomationElement;
                if (element != null)
                {
                    int processId = element.Current.ProcessId;
                    lock (intLock)
                    {
                        if (processId != lastProc)
                        {
                            lastProc = processId;
                            Logger.Log.FocusChange(processId);
                        }
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
