using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.TrayIcon
{
    [System.Runtime.InteropServices.ComVisible(true)]
    public class CallbackShim
    {
        private WindowsBrowser m_owner;
        public CallbackShim(WindowsBrowser owner)
        {
            m_owner = owner;
        }

        public void setMinSize(int w, int h)
        {
            m_owner.MinimumSize = new Size(w, h);
        }

        public void setSize(int w, int h)
        {
            m_owner.Width = w;
            m_owner.Height = h;
        }

        public void setTitle(string title)
        {
            m_owner.Text = title;
        }
        
        public void openWindow(string url)
        {
            WinFormsRunner.Instance.ShowUrlInWindow(Program.Connection.EditWindowURL);
        }
    }

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
