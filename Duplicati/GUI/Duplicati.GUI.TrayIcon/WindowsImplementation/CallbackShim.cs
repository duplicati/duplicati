using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

namespace Duplicati.GUI.TrayIcon.Windows
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
            WinFormsRunner.Instance.ShowUrlInWindow(Program.Connection.EditWindowURL + "?action=add-backup");
        }
    }
}
