using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;

namespace WMDCollector
{
    public partial class WelcomeDialog : Form
    {
        static public DialogResult Show(string vcode)
        {
            using (WelcomeDialog dialog = new WelcomeDialog(vcode))
            {
                DialogResult result = dialog.ShowDialog();
                return result;
            }
        }

        /// <summary>
        /// The private constructor. This is only called by the static method ShowDialog.
        /// </summary>
        private WelcomeDialog(string vcode)
        {
            this.Font = SystemFonts.MessageBoxFont;
            this.ForeColor = SystemColors.WindowText;
            InitializeComponent();
            // set our width and height to these values (redundant, but who cares?)
            //this.Width = 350;
            //this.Height = 150;
            using (Graphics graphics = this.CreateGraphics())
            {
                this.textBox1.Text = vcode;
                // this button must always be present
                this.buttonRight.Text = "Ok";
                this.label1.Text = "If you are seeing this for the first time, enter the VCODE as proof of task completion\n\nReminder: This application will now minimize to the taskbar. \nThe longer it runs the more you earn in bonuses!\n\nIn case you have to reboot or log off, simply start this executable again to resume.\n\nThank you for contributing to our research!";
                pictureBox1.Image = Properties.Resources.Icon.ToBitmap();
            }
        }
    }
}
