using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WMDCollector
{
    /// <summary>
    /// A utility class to determine a process parent.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ParentProcessUtilities
    {
        // These members must match PROCESS_BASIC_INFORMATION
        internal IntPtr Reserved1;
        internal IntPtr PebBaseAddress;
        internal IntPtr Reserved2_0;
        internal IntPtr Reserved2_1;
        internal IntPtr UniqueProcessId;
        internal IntPtr InheritedFromUniqueProcessId;

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ParentProcessUtilities processInformation, int processInformationLength, out int returnLength);

        /// <summary>
        /// Gets the parent process of the current process.
        /// </summary>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess()
        {
            return GetParentProcess(Process.GetCurrentProcess().Handle);
        }

        /// <summary>
        /// Gets the parent process of specified process.
        /// </summary>
        /// <param name="id">The process id.</param>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess(int id)
        {
            Process process = Process.GetProcessById(id);
            return GetParentProcess(process.Handle);
        }

        /// <summary>
        /// Gets the parent process of a specified process.
        /// </summary>
        /// <param name="handle">The process handle.</param>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess(IntPtr handle)
        {
            ParentProcessUtilities pbi = new ParentProcessUtilities();
            int returnLength;
            int status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out returnLength);
            if (status != 0)
                throw new Win32Exception(status);

            try
            {
                return Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32());
            }
            catch (ArgumentException)
            {
                // not found
                return null;
            }
        }
    }

    class Utilities
    {

        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
        public static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        [DllImport("urlmon.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false)]
        static extern int FindMimeFromData(IntPtr pBC,
              [MarshalAs(UnmanagedType.LPWStr)] string pwzUrl,
             [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I1, SizeParamIndex = 3)]
            byte[] pBuffer,
              int cbSize,
                 [MarshalAs(UnmanagedType.LPWStr)]  string pwzMimeProposed,
              int dwMimeFlags,
              out IntPtr ppwzMimeOut,
              int dwReserved);

        // reverse byte order (16-bit)
        public static UInt16 ReverseBytes(UInt16 value)
        {
            return (UInt16)((value & 0xFFU) << 8 | (value & 0xFF00U) >> 8);
        }

        // http://msdn.microsoft.com/en-us/library/ms775147%28VS.85%29.aspx#Known_MimeTypes
        [DebuggerNonUserCode]
        public static string getMimeFromFile(string file)
        {
            try
            {
                IntPtr mimeout;
                if (!System.IO.File.Exists(file))
                    throw new FileNotFoundException(file + " not found");

                int MaxContent = (int)new FileInfo(file).Length;
                if (MaxContent > 4096) MaxContent = 4096;
                FileStream fs = File.OpenRead(file);


                byte[] buf = new byte[MaxContent];
                fs.Read(buf, 0, MaxContent);
                fs.Close();
                int result = FindMimeFromData(IntPtr.Zero, file, buf, MaxContent, null, 0, out mimeout, 0);

                if (result != 0)
                    throw Marshal.GetExceptionForHR(result);
                string mime = Marshal.PtrToStringUni(mimeout);
                Marshal.FreeCoTaskMem(mimeout);
                return mime;
            }
            catch (Exception)
            {
                return null;
            }
        }
        public static Signature GetSignature(string file)
        {
            //string file = @"C:\Users\Kyle\Downloads\avg.exe";
            //file = @"C:\Program Files\VMware\VMware Tools\vmtoolsd.exe";
            try
            {
                X509Certificate cert1 = X509Certificate.CreateFromSignedFile(file);
                Dictionary<string, object> info = new Dictionary<string, object>();
                String subjectName = cert1.Subject;
                String issuerName = cert1.Issuer;
                // CN="VMware, Inc.", OU=Marketing, OU=Digital ID Class 3 - Microsoft Software Validation v2, O="VMware, Inc.", L=Palo Alto, S=California, C=US
                // CN=Cortado AG, OU=Digital ID Class 3 - Microsoft Software Validation v2, O=Cortado AG, L=Berlin, S=Berlin, C=DE
                // Extract the subject
                Match matchWithoutQuotes = Regex.Match(subjectName, @"CN=([^,]*),.*");

                Match matchWithQuotes = Regex.Match(subjectName, @"CN=""([^""]*)"",.*");

                if (matchWithQuotes.Success)
                {
                    subjectName = matchWithQuotes.Groups[1].Value;
                }
                else if (matchWithoutQuotes.Success)
                {
                    subjectName = matchWithoutQuotes.Groups[1].Value;
                }
                // Extract the issuer
                matchWithoutQuotes = Regex.Match(issuerName, @"CN=([^,]*),.*");

                matchWithQuotes = Regex.Match(issuerName, @"CN=""([^""]*)"",.*");

                if (matchWithQuotes.Success)
                {
                    issuerName = matchWithQuotes.Groups[1].Value;
                }
                else if (matchWithoutQuotes.Success)
                {
                    issuerName = matchWithoutQuotes.Groups[1].Value;
                }
                return new Signature(subjectName, issuerName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        [DebuggerNonUserCode]
        public static string ComputeMD5(String filePath)
        {
            string result = null;
            try
            {
                // Don't hash a file if its greater than 100MB. 
                if (new System.IO.FileInfo(filePath).Length > 104857600) return "";
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        result = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
                    }
                }
            }
            catch (FileNotFoundException)
            {
                result = "not_found";
            }
            catch (IOException)
            {
                result = "locked";
            }
            catch (Exception)
            {
                result = null;
            }
            finally
            {
            }
            return result;
        }
        public static long GetFileSize(String filePath)
        {
            try
            {
                return new System.IO.FileInfo(filePath).Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }
        /// <summary>
        /// Given two DateTime ticks, computes the # of seconds that has elapsed
        /// </summary>
        public static int ElapsedTime(long start, long end)
        {
            long duration = end - start;
            Debug.Assert(duration >= 0);
            return (int)(duration / (1000 * 10000L));
        }

        /// <summary>
        /// Returns the current time in terms of # of 100ns units from the year 1601, UTC time
        /// </summary>
        public static long GetCurrentTime()
        {
            TimeSpan ts = DateTime.UtcNow - new DateTime(1601, 1, 1);
            return ts.Ticks;
        }
        public static long ConvertTo1601Time(DateTime localTime)
        {
            TimeSpan ts = TimeZoneInfo.ConvertTimeToUtc(localTime) - new DateTime(1601, 1, 1);
            return ts.Ticks;
        }

        public static string StringComputeMD5(string input)
        {
            // step 1, calculate MD5 hash from input
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hash = md5.ComputeHash(inputBytes);

                // step 2, convert byte array to hex string
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }
        public static string StringComputeHMACMD5(string input, string key)
        {
            byte[] keyBytes = System.Text.Encoding.ASCII.GetBytes(key);
            using (var md5 = new System.Security.Cryptography.HMACMD5(keyBytes))
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hash = md5.ComputeHash(inputBytes);

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }


        // The image file paths given by ImageLoadTraceData objects are strange.
        // They don't include a logical drive and might start with a MS-DOS device name
        // such as \Device\HarddiskVolume1 or SystemRoot.
        // Transforms the given path to one that includes the logical drive. 
        public static string TransformFilePath(String path)
        {

            var logicalDrives = new Dictionary<string, string>();
            foreach (DriveInfo drive in System.IO.DriveInfo.GetDrives())
            {
                String driveLetter = drive.Name.TrimEnd('\\');
                logicalDrives.Add(GetDrivePath(driveLetter), driveLetter);
            }

            // First, check to see if the path begins with a drive letter
            // If it does, all is good. 
            foreach (String drive in logicalDrives.Values)
            {
                if (path.StartsWith(drive))
                {
                    return path;
                }
            }

            // Check if it begins with a MS-DOS device name
            foreach (String driveName in logicalDrives.Keys)
            {
                if (path.StartsWith(driveName))
                {
                    return ReplaceFirst(path, driveName, logicalDrives[driveName]);
                }
            }

            //TODO technically we should have a check that this is not actually a directory. But its not very likely.
            if (path.StartsWith(@"\SystemRoot"))
            {
                return ReplaceFirst(path, @"\SystemRoot", Environment.GetEnvironmentVariable("SYSTEMROOT"));
            }// Else default to the system drive
            else
            {
                // The %SystemDrive% variable is a special system-wide environment variable found on Microsoft Windows NT 
                // and its derivatives. Its value is the drive upon which the system folder was placed
                return Environment.ExpandEnvironmentVariables("%SystemDrive%") + path;
            }
        }


        private static string ReplaceFirst(string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
            {
                return text;
            }
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        private static string GetDrivePath(String driveLetter)
        {
            StringBuilder pathInformation = new StringBuilder(250);
            QueryDosDevice(driveLetter, pathInformation, 250);
            return pathInformation.ToString();
        }
    }
}
