using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Security.Cryptography;

namespace WMDCollector
{
    class ProcHolder
    {
        public int Id { get; private set; }
        public DateTime StartTime { get; private set; }
        public ProcHolder(int pid, DateTime start)
        {
            Id = pid;
            StartTime = start;
        }
    }

    public class Signature
    {
        public String subj { get; private set; }
        public String issuer { get; private set; }

        public Signature(String theSubj, String theIssuer)
        {
            subj = theSubj;
            issuer = theIssuer;
        }
    }

    public class ProcessEnd : Event
    {
        public SystemProcess Process { get; private set; }
        public int ExitCode { get; private set; }
        public int WriteOps { get; private set; }

        public ProcessEnd(SystemProcess proc, long endTime, int writeOps, int exitCode)
            : base(endTime)
        {
            this.Process = proc;
            this.ExitCode = exitCode;
            this.WriteOps = writeOps;
        }
        override public string ToString()
        {
            Dictionary<string, object> info = new Dictionary<string, object>();
            info.Add("_t", "eventlogger.ProcessEnd");
            info.Add("_id", this.Guid.ToString());
            info.Add("procId", this.Process.StartEvent.Guid.ToString());
            info.Add("exitCode", this.ExitCode);
            info.Add("writeOps", this.WriteOps);
            info.Add("numConn", this.Process.NumConnections);
            info.Add("time", this.Time);

            string json = JsonConvert.SerializeObject(info, Formatting.None);
            return json;
        }

    }

    public class ProcessStart : Event
    {
        public SystemProcess Process { get; private set; }
        public String Hash { get; private set; }
        public Signature Sig { get; private set; }
        private string excuse;

        public ProcessStart(SystemProcess proc, long startTime)
            : base(startTime)
        {
            this.Process = proc;
        }

        override public void FinishProcessing()
        {
            this.Hash = ExecutableManager.GetInstance().GetHash(Process.ImagePath);
            this.Sig = ExecutableManager.GetInstance().GetSignature(Process.ImagePath);

            if (this.Hash != null && (this.Hash.Equals("not_found") || this.Hash.Equals("locked")))
            {
                this.excuse = this.Hash;
                this.Hash = null;
            }

        }

        override public string ToString()
        {
            Dictionary<string, object> info = new Dictionary<string, object>();
            info.Add("_t", "eventlogger.ProcessStart");
            info.Add("_id", this.Guid.ToString());

            if (this.Process.Parent != null)
            {
                info.Add("parentId", this.Process.Parent.StartEvent.Guid.ToString());
            }
            info.Add("pid", this.Process.ProcessId);
            info.Add("image", this.Process.ImagePath);

            if (this.excuse != null)
            {
                info.Add("excuse", this.excuse);
            }
            if (this.Hash != null)
            {
                info.Add("hash", this.Hash);
            }
            if (this.Sig != null)
            {
                info.Add("sig", this.Sig);
            }
            info.Add("time", this.Time);
            string json = JsonConvert.SerializeObject(info, Formatting.None);
            return json;
        }
    }

    /// <summary>
    /// Tracks all relevant information and events regarding a particular process
    /// </summary>
    public class SystemProcess
    {
        public int ProcessId { get; private set; }
        public string ImagePath { get; private set; }

        // Stores a list of new files 
        private Dictionary<ulong, NewFile> newFiles;

        private Dictionary<ConnectionMetadata, Connection> connections;

        public ProcessStart StartEvent;

        public ProcessEnd EndEvent;

        public SystemProcess Parent;
        public int NumConnections { get; private set; }

        public SystemProcess(SystemProcess parent, long  startTime, int procId, string imagePath)
        {
            this.Parent = parent;
            this.ProcessId = procId;
            this.ImagePath = imagePath;
            this.connections = new Dictionary<ConnectionMetadata, Connection>(500);
            this.NumConnections = 0;
            this.newFiles = new Dictionary<ulong, NewFile>(500);
            
            // Create an event based on the process starting
            this.StartEvent = new ProcessStart(this, startTime);
        }

        public void Terminate(long endTime, int writeOps, int exitCode)
        {
            this.EndEvent = new ProcessEnd(this, endTime, writeOps, exitCode);
        }

        public IEnumerable<Connection> GetConnections()
        {
            return this.connections.Values;
        }

        public Connection GetExistingConnection(IPEndPoint local, IPEndPoint remote, String protocol)
        {
            ConnectionMetadata metadata = new ConnectionMetadata(local, remote, protocol);
            if (this.connections.ContainsKey(metadata)) return this.connections[metadata];
            else return null;
        }


        public Connection AddConnection(long time, string prot, IPEndPoint local, IPEndPoint remote, bool isInbound)
        {
            NumConnections += 1;
            ConnectionMetadata metadata = new ConnectionMetadata(local, remote, prot);
            Connection conn = new Connection(this, prot, local, remote, isInbound, time);
            this.connections[metadata] = conn;
            return conn;
        }

        public void Terminate(ConnectionMetadata metadata)
        {
            this.connections.Remove(metadata);
        }

        public List<NewFile> GetFiles()
        {
            List<NewFile> files = new List<NewFile>();
            files.AddRange(this.newFiles.Values);
            return files;
        }

        public void AddFileEvent(NewFile fileEvent)
        {
            if (!this.newFiles.ContainsKey(fileEvent.FileObject))
            {
                this.newFiles.Add(fileEvent.FileObject, fileEvent);
            }
        }

        public NewFile GetExistingFile(ulong fileObj)
        {
            if (this.newFiles.ContainsKey(fileObj)) return this.newFiles[fileObj];
            else return null;
        }

        public NewFile CloseFile(ulong fileObj)
        {
            NewFile file = null;
            if (this.newFiles.ContainsKey(fileObj))
            {
                file = this.newFiles[fileObj];
                if (file != null && !file.IsClosed())
                {
                    // Now remove the file! 
                    this.newFiles.Remove(fileObj);
                    // Marks the file as closed
                    file.Cleanup();
                }
            }
            return file;
        }
        public override string ToString()
        {
            if (this.Parent == null)
            {
                return String.Format("Pid: {0}, Parent Pid: None", this.ProcessId);
            }
            else
            {
                return String.Format("Pid: {0}, Parent Pid: {1}", this.ProcessId, this.Parent.ProcessId);
            }
        }
    }
}
