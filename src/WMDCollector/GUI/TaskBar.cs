using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace WMDCollector
{
    public class TaskBar : ApplicationContext
    {
        NotifyIcon notifyIcon = new NotifyIcon();

        public TaskBar()
        {
            MenuItem exitMenuItem = new MenuItem("Exit", new EventHandler(Exit));
            notifyIcon.Icon = WMDCollector.Properties.Resources.Icon;
            notifyIcon.DoubleClick += new EventHandler(ShowMessage);
            notifyIcon.ContextMenu = new ContextMenu(new MenuItem[] { exitMenuItem });
            notifyIcon.Visible = true;
        }

        void ShowMessage(object sender, EventArgs e)
        {
            // Only show the message if the settings say we can.
            //if (TaskTrayApplication.Properties.Settings.Default.ShowMessage)
            MessageBox.Show("Hello World");
        }


        void Exit(object sender, EventArgs e)
        {
            // We must manually tidy up and remove the icon before we exit.
            // Otherwise it will be left behind until the user mouses over.
            notifyIcon.Visible = false;
            Application.Exit();
        }
    }
}
