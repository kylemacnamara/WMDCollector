using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Net;
using System.Diagnostics;

namespace WMDCollector
{
    /// <summary>
    /// A singleton in charge of caching signatures and hashes for performance reasons
    /// </summary>
    class ExecutableManager
    {
        private static volatile ExecutableManager instance;
        private static object syncRoot = new Object();
        private ConcurrentDictionary<String, Signature> signatures;
        private ConcurrentDictionary<String, String> hashes;

        public ExecutableManager()
        {
            signatures = new ConcurrentDictionary<string, Signature>();
            hashes = new ConcurrentDictionary<string, string>();
        }

        public String GetHash(String filePath)
        {
            try
            {
                if (filePath == null) return null;
                if (!hashes.ContainsKey(filePath))
                {
                    hashes[filePath] = Utilities.ComputeMD5(filePath);
                }
                return hashes[filePath];
            }
            catch (Exception)
            {
                Debug.Assert(false);
                return null;
            }
        }

        public Signature GetSignature(String filePath)
        {
            try
            {
                if (filePath == null) return null;
                if (!signatures.ContainsKey(filePath))
                {
                    signatures[filePath] = Utilities.GetSignature(filePath);
                }
                return signatures[filePath];
            }
            catch (Exception)
            {
                Debug.Assert(false);
                return null;
            }
        }

        public static ExecutableManager GetInstance()
        {
            if (instance == null)
            {
                lock (syncRoot)
                {
                    if (instance == null) 
                        instance = new ExecutableManager();
                }
            }
            return instance;
        }
      
    }
}
