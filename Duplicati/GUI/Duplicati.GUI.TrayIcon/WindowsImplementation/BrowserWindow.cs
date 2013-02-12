using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.TrayIcon.Windows
{
    public partial class WindowsBrowser : Form, IBrowserWindow
    {
        public WindowsBrowser(string url)
            : this()
        {
            Browser.ObjectForScripting = new CallbackShim(this);
            Browser.Url = new Uri(url);
            Browser.Visible = false;
            Browser.DocumentCompleted += new WebBrowserDocumentCompletedEventHandler(OnBrowserLoad);  
        }

        private void OnBrowserLoad(object target, WebBrowserDocumentCompletedEventArgs a)
        {
            Browser.Visible = true;
        }

        public WindowsBrowser()
        {
            InitializeComponent();
        }

        public string Title
        {
            set
            {
                this.Text = value;
            }
        }

        public new WindowIcons Icon
        {
            set
            {
                switch (value)
                {
                    case  WindowIcons.LogWindow:
                        base.Icon = Properties.Resources.TrayNormal;
                        break;
                    default:
                        base.Icon = Properties.Resources.TrayNormal;
                        break;
                }
            }
        }
    }
}
