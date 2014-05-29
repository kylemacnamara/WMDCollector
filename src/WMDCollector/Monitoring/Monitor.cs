using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Diagnostics.Tracing.Parsers;
using Diagnostics.Tracing;
using System.IO;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Process;
using Microsoft.Diagnostics.Tracing.Parsers.Network;
using Microsoft.Diagnostics.Tracing.Parsers.File;
using Microsoft.Diagnostics.Tracing.Parsers.Focus;
using System.Management;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace WMDCollector
{
    class Monitor
    {
        private ETWTraceEventSource source;
        private TraceEventSession session;

        private EventBuffer buffer;
        // maps process ids to the actual process objects
        private Dictionary<int, SystemProcess> runningProcesses;
        private NetworkManager networkManager;
        // Used to create a HeartBeat event every 30 seconds
        private Timer heartBeatTimer;

        private long lastFileCheckTime;
        private long lastGarbageCollect;
        public Monitor()
        {
            Process parentProc = ParentProcessUtilities.GetParentProcess();
            if (parentProc != null)
            {
                parentProc.EnableRaisingEvents = true;
                parentProc.Exited += delegate(object sender, EventArgs e)
                {
                    if (this.session != null)
                    {
                        this.session.Stop();
                    }
                };
            }

            buffer = new EventBuffer();
            // Initialize the list of running processes with those that are already running. 
            runningProcesses = GetCurrentProcesses();
            foreach (SystemProcess proc in runningProcesses.Values)
            {
                buffer.AddEvent(proc.StartEvent);
            }
            networkManager = new NetworkManager();
          
            heartBeatTimer = new Timer(HeartBeatEvent, null, 0, 30 * 1000);
            lastFileCheckTime = Utilities.GetCurrentTime();
            lastGarbageCollect = Utilities.GetCurrentTime();
        }

        public void HeartBeatEvent(object sender)
        {
            this.buffer.AddEvent(new HeartBeat(Utilities.GetCurrentTime()));
        }

        private List<ProcHolder> GetProcesses()
        {
            List<ProcHolder> procs = new List<ProcHolder>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    procs.Add(new ProcHolder(p.Id, p.StartTime));

                }
                catch (Exception) { }
            }
            return procs;
        }
 
        public Dictionary<int, SystemProcess> GetCurrentProcesses()
        {
            var currentProcs = new Dictionary<int, SystemProcess>();
            SystemProcess newProc;

            var wmiQueryString = "SELECT Name, ProcessId, ExecutablePath, CommandLine FROM Win32_Process";
            using (var searcher = new ManagementObjectSearcher(wmiQueryString))
            using (var results = searcher.Get())
            {
                var query = from p in GetProcesses()
                            join mo in results.Cast<ManagementObject>()
                            on p.Id equals (int)(uint)mo["ProcessId"]
                            where IsValidProcess(p, mo)
                            orderby p.StartTime
                            select new
                            {
                                StartTime = (p.StartTime),
                                Id = p.Id,
                                Path = (string)mo["ExecutablePath"],
                                Name = (string)mo["Name"]
                            };
                foreach (var item in query)
                {
                    try {
                        // item.Path can be null, if it is a Windows process
                        string imagePath = (item.Path != null) ? item.Path : (String)item.Name;
                        int pid = item.Id;
                        DateTime start = item.StartTime;
                        long convertedTime = Utilities.ConvertTo1601Time(start);
                        // Get Parent process. It will have started earlier so it must already exist. 
                        System.Diagnostics.Process parentProc = null;
                  
                        parentProc = ParentProcessUtilities.GetParentProcess(pid);
                        if (parentProc != null)
                        {
                            SystemProcess parentData;
                            currentProcs.TryGetValue(parentProc.Id, out parentData);
                            newProc = new SystemProcess(parentData, convertedTime, pid, imagePath);
                        }
                        else
                        {
                            newProc = new SystemProcess(null, convertedTime, pid, imagePath);
                        }
                        currentProcs.Add(newProc.ProcessId, newProc);
                    }
                    catch (Exception) { }
                }
            }
            return currentProcs;

        }
        
        /// <summary>
        /// Checks to see if the process is a valid process that will not generate an Access Denied exception when trying to access
        /// certain properties of the process such as the time. 
        /// </summary>
        private bool IsValidProcess (ProcHolder proc, ManagementObject mo)
        {
            try
            {
                int pid = proc.Id;
                DateTime time = proc.StartTime;
                string path = (string)mo["ExecutablePath"];
                string name = (string)mo["Name"];
                return ((path != null || name != null) && time != null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void EnableProviders()
        {
            // Enable the Microsoft-Windows-Kernel-File provider
            this.session.EnableProvider(new Guid("EDD08927-9CC4-4E65-B970-C2560FB5C289"), TraceEventLevel.Always, 0x20 | 0x800 | 0x1000);
            // Enable the Microsoft-Windows-Kernel-Process provider
            this.session.EnableProvider(new Guid("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716"), TraceEventLevel.Always, 0x10);
            // Enable the Microsoft-Windows-Kernel-Network provider
            this.session.EnableProvider(new Guid("7DD42A49-5329-4832-8DFD-43D979153A88"));
            // Enable the dynamic provider!
            this.session.EnableProvider(new Guid("c6a6890e-d1d1-53ca-7869-b836a18d0650")); 
        }


        public void BeginMonitoring()
        {
            var sessionName = "SecLabLogging";
            using (var session = new TraceEventSession(sessionName, null))
            {
                this.session = session;
                session.StopOnDispose = true;
                using (var source = new ETWTraceEventSource(sessionName, TraceEventSourceType.Session))
                {
                    this.source = source;
                    var fileParser = new FileTraceEventParser(source);
                    var networkParser = new NetworkTraceEventParser(source);
                    var processParser = new ProcessTraceEventParser(source);
                    var focusParser = new FocusTraceEventParser(source);

                    // Handle process related events
                    processParser.ProcessStart += HandleProcessStart;
                    processParser.ProcessStop += HandleProcessStop;

                    // Handle network related events
                    networkParser.KERNEL_NETWORK_TASK_TCPIPDataReceived += HandleTCP;
                    networkParser.KERNEL_NETWORK_TASK_TCPIPDataSent += HandleTCP;
                    networkParser.KERNEL_NETWORK_TASK_UDPIPDataReceivedOverUDPProtocol += HandleUDP;
                    networkParser.KERNEL_NETWORK_TASK_UDPIPDataSentOverUDPProtocol += HandleUDP;
                    networkParser.KERNEL_NETWORK_TASK_TCPIPDisconnect += HandleTCPDisconnect;

                    // Handle file related events
                    fileParser.CreateNewFile += HandleNewFile;
                    fileParser.Cleanup += HandleFileCleanup;
                    fileParser.RenamePath += HandleFileRename;
       
                    focusParser.FocusChange += delegate(FocusTraceData data)
                    {
                        long timestamp = data.TimeStamp100ns;
                        Preprocessing(timestamp);
                        SystemProcess curProc;
                        runningProcesses.TryGetValue(data.PID, out curProc);
                        if (curProc != null)
                        {
                            this.buffer.AddEvent(new FocusChange(curProc, timestamp));
                        }
                    };
                    this.EnableProviders();
                    source.Process();
                }
            }
        }

        public void HandleNewFile(CreateTraceData data)
        {
            SystemProcess curProc;
            runningProcesses.TryGetValue(data.ProcessID, out curProc);
            if (curProc != null)
            {
                String filePath = Utilities.TransformFilePath(data.FileName);
                // Attempt to ensure it is not a directory
                // Don't go to disk if we don't have to!
                if (Path.HasExtension(filePath) || !Directory.Exists(filePath))
                {
                    long timestamp = data.TimeStamp100ns;
                    Preprocessing(timestamp);
                    curProc.AddFileEvent(new NewFile(curProc, filePath, timestamp, data.FileObject));
                }
            }
        }

        public void HandleFileCleanup(CleanupTraceData data)
        {
            SystemProcess curProc;
            runningProcesses.TryGetValue(data.ProcessID, out curProc);
            if (curProc != null)
            {
                Event fileEvent = curProc.CloseFile(data.FileObject);
               
                if (fileEvent != null)
                {
                    long timestamp = data.TimeStamp100ns;
                    Preprocessing(timestamp);
                    var file = (NewFile)fileEvent;
                    file.LastModified = timestamp;
                    // The file might have been sent to the server already
                    // If so, lets create a FileUpdate event instead!

                    if (file.SentToServer)
                    {
                        buffer.AddEvent(new FileUpdate(file));
                    }
                    else
                    {
                        file.SentToServer = true;
                        buffer.AddEvent(fileEvent);
                    }

                }
            }
        }

        public void HandleFileRename(DeletePathTraceData data)
        {
            SystemProcess curProc;
            runningProcesses.TryGetValue(data.ProcessID, out curProc);
            if (curProc != null)
            {
                long timestamp = data.TimeStamp100ns;
                Preprocessing(timestamp);
                String filename = Utilities.TransformFilePath(data.FilePath);
                curProc.AddFileEvent(new NewFile(curProc, filename, timestamp, data.FileObject));
            }
        }


        private void HandleTraffic(NetworkTraceData data, String protocol)
        {
            try
            {
                SystemProcess curProc;
                runningProcesses.TryGetValue(data.PID, out curProc);

                if (curProc != null)
                {
                    long timestamp = data.TimeStamp100ns;
                    Preprocessing(timestamp);
                    IPAddress saddr = new IPAddress((UInt32)(Int32)data.saddr);
                    IPAddress daddr = new IPAddress((UInt32)(Int32)data.daddr);
                    UInt16 sport = Utilities.ReverseBytes((UInt16)(Int32)data.sport);
                    UInt16 dport = Utilities.ReverseBytes((UInt16)(Int32)data.dport);

                    // By default, source address is local. True for TCP. 
                    IPEndPoint local = new IPEndPoint(saddr, sport);
                    IPEndPoint remote = new IPEndPoint(daddr, dport);

                    if (protocol == Protocol.UDP)
                    {
                        if (networkManager.IsLocal(daddr))
                        {
                            // Switch the end points. The destination seems to be local instead. 
                            IPEndPoint tmp = remote;
                            remote = local;
                            local = tmp;
                        }
                    }

                    bool isInbound = false;
                    
                   int bytesReceived = 0;
                   int bytesSent = 0;
                   Connection existingConn = curProc.GetExistingConnection(local, remote, protocol);
                   if ( existingConn == null) 
                   {
                       
                       if (protocol == Protocol.TCP)
                       {
                           IPEndPoint[] listeners = networkManager.GetListeners(protocol);
                           IPEndPoint nonRoutable = new IPEndPoint(IPAddress.Parse("0.0.0.0"), local.Port);
                           if (listeners.Contains(local) ||
                               listeners.Contains(nonRoutable))
                           {
                               isInbound = true;
                           }
                       }
                       Connection newlyAdded =  curProc.AddConnection(timestamp, protocol, local, remote, isInbound);
                       this.buffer.AddEvent(newlyAdded);
                   } else {
                       // Its an existing connection
                       if (data.EventName.Contains("sent"))
                       {
                           bytesSent = data.size;
                       }
                       else if (data.EventName.Contains("received"))
                       {
                           bytesReceived = data.size;
                       }
                       existingConn.ReceivedAdditionalBytes(bytesReceived, timestamp);
                       existingConn.SentAdditionalBytes(bytesSent, timestamp);
                   }
                }
            }
            catch (Exception) { }
        }

        public void HandleTCP(NetworkTraceData data)
        {
            HandleTraffic(data, Protocol.TCP);
        }

        public void HandleUDP(NetworkTraceData data)
        {
            HandleTraffic(data, Protocol.UDP);
        }

        public void HandleTCPDisconnect(NetworkTraceData data)
        {
            int pid = data.PID;
            SystemProcess curProc;
            runningProcesses.TryGetValue(pid, out curProc);
            if (curProc != null)
            {
                IPAddress saddr = new IPAddress((UInt32)(Int32)data.saddr);
                IPAddress daddr = new IPAddress((UInt32)(Int32)data.daddr);
                UInt16 sport = Utilities.ReverseBytes((UInt16)(Int32)data.sport);
                UInt16 dport = Utilities.ReverseBytes((UInt16)(Int32)data.dport);

                IPEndPoint local = new IPEndPoint(saddr, sport);
                IPEndPoint remote = new IPEndPoint(daddr, dport);

                Connection existingConn = curProc.GetExistingConnection(local, remote, Protocol.TCP);
                if (existingConn != null)
                {
                    this.buffer.AddEvent(existingConn.ReceiveUpdate(data.TimeStamp100ns));
                    // Terminate connection
                    curProc.Terminate(new ConnectionMetadata(local, remote, Protocol.TCP));
                }
            }
        }

        private void Preprocessing(long currentTime)
        {
            MonitorFiles(currentTime);
            MonitorUDPConnections(currentTime);
            GarbageCollectionProcesses(currentTime);
        }

        private void MonitorFiles(long currentTime)
        {
            int elapsedTime = Utilities.ElapsedTime(lastFileCheckTime, currentTime);
            if (elapsedTime >= 5)
            {
                lastFileCheckTime = currentTime;
                foreach (SystemProcess proc in runningProcesses.Values)
                {
                    foreach (NewFile file in proc.GetFiles())
                    {
                        // Only consider this file if we have not yet sent it to the server and 
                        int timeSinceModification = Utilities.ElapsedTime(file.LastModified, currentTime);
                        if (file.SentToServer == false && (timeSinceModification >= 10))
                        {
                            file.SentToServer = true;
                            this.buffer.AddEvent(file);
                        }
                    }
                }
            }
        }

        private void GarbageCollectionProcesses(long currentTime)
        {
            int elapsedTime = Utilities.ElapsedTime(lastGarbageCollect, currentTime);
            if (elapsedTime >= 30)
            {
                foreach (var item in runningProcesses.Where(x => ExpiredProcess(x.Value, currentTime)).ToList())
                {
                    runningProcesses.Remove(item.Key);
                }
                lastGarbageCollect = currentTime;

            }
        }
        private bool ExpiredProcess(SystemProcess proc, long currentTime)
        {
            return proc.EndEvent != null &&
               (Utilities.ElapsedTime(proc.EndEvent.Time, currentTime) >= 30);      
        }

        /// <summary>
        /// Flushes UDP connections if they need to be flushed. This is done because ETW does not generate
        /// an event that signals the end of a UDP connection, only for TCP connections. 
        /// </summary>
        private void MonitorUDPConnections(long currentTime)
        {
            int elapsedTime = Utilities.ElapsedTime(networkManager.LastFlushedConnections, currentTime);
            if (elapsedTime >= 5)
            {
                networkManager.LastFlushedConnections = currentTime;

                foreach (SystemProcess proc in this.runningProcesses.Values)
                {
                    foreach (Connection conn in proc.GetConnections())
                    {
                        if (conn.Proto.Equals("UDP"))
                        {
                            ConnectionUpdate update = conn.ReceiveUpdate(currentTime);
                            if (update != null)
                            {
                                this.buffer.AddEvent(update);
                            }
                        }
                    }
                }
            }
        }

        public void HandleProcessStart(ProcessStartTraceData data)
        {
            long timestamp = data.TimeStamp100ns;
            Preprocessing(timestamp);
            int procId = data.ProcessID;
            // Check to make sure there is no "running" process with the same id
            // If there is, kick them out
            if (runningProcesses.ContainsKey(procId))
            {
                runningProcesses.Remove(procId);
            }
            int parentId = data.ParentProcessID;
            string filePath = Utilities.TransformFilePath(data.ImageName);
            // Look up parent process
            SystemProcess parent;
            runningProcesses.TryGetValue(data.ParentProcessID, out parent);
            SystemProcess newProc = new SystemProcess(parent, timestamp, procId, filePath);
            runningProcesses.Add(procId, newProc);
            // Add the start event to buffer
            this.buffer.AddEvent(newProc.StartEvent);
        }

        public void HandleProcessStop(ProcessStopTraceData data)
        {
            long timestamp = data.TimeStamp100ns;
            Preprocessing(timestamp);
            int procId = data.ProcessID;
            SystemProcess curProc;
            runningProcesses.TryGetValue(procId, out curProc);
            if (curProc != null)
            {
                // Terminate the process
                curProc.Terminate(timestamp, data.WriteOperationCount, data.ExitCode);
                buffer.AddEvent(curProc.EndEvent);
            }
        }
    }
}
