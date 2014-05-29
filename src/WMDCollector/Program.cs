using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using System.Net;
using System.Security.Principal;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using Microsoft.Win32.TaskScheduler;

namespace WMDCollector
{
    static class Program
    {
        /// <summary>
        /// The application uses two processes. The parent process creates a child process. The child process does all the work and the parent process's only purpose is to restart
        /// the child process if for some unforseen reason the process is terminated. 
        /// </summary>
        static void CreateAndMonitorMainProcess()
        {
            // Start the main child process
            ProcessStartInfo proc = new ProcessStartInfo(Application.ExecutablePath, "Monitor")
            {
                Verb = "runas",
                UseShellExecute = true
            };
            Process childProcess = Process.Start(proc);
            childProcess.EnableRaisingEvents = true;
            childProcess.Exited += delegate(object sender, EventArgs e)
            {
        
                Console.WriteLine("child has terminated!");
                System.Threading.Thread.Sleep(5000);
                CreateAndMonitorMainProcess();
            };

        }
        public static bool IsWindows7()
        {
            return (Environment.OSVersion.Version.Major == 6 &&
                Environment.OSVersion.Version.Minor == 1);

        }

        /// <summary>
        /// Checks to see if the program meets the minimum requirements and should continue running. 
        /// </summary>
        public static void CheckRequirements()
        {
            if (!IsWindows7())
            {
                MessageBox.Show("Error: Sorry, but you need Windows 7 to run this");
                Environment.Exit(1);
            }
            int procCount = Environment.ProcessorCount;
            ulong ramBytes = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory;
            if (procCount < 2 || ramBytes < 2684354560)
            {
                MessageBox.Show("Error: Sorry, but your computer does not meet the minimum performance requirements");
                Environment.Exit(1);
            }

            // Check that we have admin access
            bool isElevated;
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            if (!isElevated)
            {
                Console.WriteLine("Exiting!");
                MessageBox.Show("Error: You need administrator access \n\nFix: Hold shift, then right click the executable. Then choose 'Run as different user.' Lastly, enter the credentials of an administrator account");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            CheckRequirements();

            // Catch all Unhandled exceptions to prevent the "Error" box from appearing.
            // Posts this error to the Server using the error url. 
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs arguments)
            {
                Console.WriteLine("UNHANDLED EXCEPTION!!!");
                Console.WriteLine(arguments.ExceptionObject.ToString());
                HttpCollector errorReporter = new HttpCollector();
                errorReporter.PostError(arguments.ExceptionObject.ToString());
                Environment.Exit(1);
            };

            // Process responsible for displaying the GUI in the taskbar and monitoring the subprocesses that do all the actual work
            if (args.Length == 0)
            {
                // Ensure that only one instance of the program is running
                bool SingleInstance;
                var mut = new Mutex(true, "SeclabEventLogger", out SingleInstance);
                if (!SingleInstance)
                {
                    MessageBox.Show("Another instance is already running. \nCheck the taskbar.");
                    Environment.Exit(1);
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
  
                CreateAndMonitorMainProcess();
        
                Application.Run(new TaskBar());

            }
            else
            {
                MonitorWorker workerObject = new MonitorWorker();
                Thread workerThread = new Thread(workerObject.DoWork);
                workerThread.Start();
                FocusTracker tracker = new FocusTracker();
                tracker.TrackFocus();
            }
        }
    }
    class MonitorWorker
    {
        public void DoWork()
        {
            Monitor m = new Monitor();
            m.BeginMonitoring();
        }
    }
}