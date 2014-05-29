using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Policy;
using System.Security;
using System.Text;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace WMDCollector
{
    /// <summary>
    /// A FileUpdate can be created when a file event has already been sent to the server. 
    /// This can happen when a timeout occured (e.g. the file was not closed properly)
    /// </summary>
    public class FileUpdate : Event
    {
        private NewFile newFile;
        public FileUpdate(NewFile file)
            : base(file.Time)
        {
            newFile = file;
            // Ensure that it has the same guid
            this.Guid = file.Guid;
        }
        override public void FinishProcessing()
        {
            newFile.FinishProcessing();
        }  
        override public string ToString()
        {
            Dictionary<string, object> info = newFile.GetJson();
            info["_t"] = this.GetType().Name;
            string json = JsonConvert.SerializeObject(info, Formatting.None);
            return json;
        }

    }

    public class NewFile : Event
    {
        public UInt64 FileObject { get; private set; }

        public string FilePath { get; set; }

        public string Hash { get; private set; }

        // The type of the file 
        public string Type { get; private set; }

        public SecurityZone ZoneId { get; private set; }

        public long Size { get; set; }

        private Boolean closed;

        private SystemProcess process;

        public long LastModified { get; set; }

        public bool SentToServer { get; set; }

        private string excuse;
        private Object _lock = new Object();

        /// <summary>
        /// Triggered by a CreateNewFile event of the Microsoft-Windows-Kernel-File provider
        /// or triggered by a RenamePath event of the Microsoft-Windows-Kernel-File provider
        /// </summary> 
        public NewFile(SystemProcess proc, string filePath, long time, UInt64 fileObj)
            : base(time)
        {
            this.process = proc;
            this.FilePath = filePath;
            this.FileObject = fileObj;
            this.closed = false;
            this.LastModified = time;
            this.SentToServer = false;
        }

        override public void FinishProcessing()
        {
            // Acquire a lock to avoid the case of a NewFile and a FileUpdate event both being 
            // processed by the threadpool at the same time!
            lock (this._lock)
            {
                this.ZoneId = Zone.CreateFromUrl(this.FilePath).SecurityZone;
                // If file still exists, try to hash it, file type it, and size it!
                if (File.Exists(this.FilePath))
                {

                    this.Type = Utilities.getMimeFromFile(this.FilePath);
                    this.Hash = Utilities.ComputeMD5(this.FilePath);
                    this.Size = Utilities.GetFileSize(this.FilePath);
                }
                else
                {
                    this.Hash = "not_found";
                    this.Size = 0;
                }

                if (this.Hash != null && (this.Hash.Equals("not_found") || this.Hash.Equals("locked")))
                {
                    this.excuse = this.Hash;
                    this.Hash = null;
                }
            }
        }
   
        public void Cleanup()
        {

            this.FileObject = 0;
            this.closed = true;
        }

        public bool IsClosed()
        {
            return this.closed;
        }

        public Dictionary<string, object> GetJson()
        {
            Dictionary<string, object> info = new Dictionary<string, object>();
            info.Add("_t", "eventlogger.NewFile");
            info.Add("_id", this.Guid.ToString());
            info.Add("procId", this.process.StartEvent.Guid.ToString());
            info.Add("path", this.FilePath);
            if (this.Type != null)
            {
                info.Add("fileType", this.Type);
            }
            if (this.excuse != null) {
                info.Add("excuse", this.excuse);
            }
            if (this.Hash != null)
            {
                info.Add("hash", this.Hash);
            }
            info.Add("zone", this.ZoneId.ToString());
            if (this.Size != 0)
            {
                info.Add("size", this.Size);
            }
            info.Add("time", this.Time);

            if (this.LastModified != 0L)
            {
                info.Add("timeEnd", this.LastModified);
            }
            return info;
        }

        override public string ToString()
        {
            string json = JsonConvert.SerializeObject(GetJson(), Formatting.None);
            return json;
        }
    }
}
