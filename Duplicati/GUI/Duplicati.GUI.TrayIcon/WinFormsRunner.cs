// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.GUI.TrayIcon.Windows
{
    //We use a separate class to start the runner to avoid attempts to load WinForms from Program.cs
    public class WinFormsRunner : TrayIconBase
    {
        private class MenuItemWrapper : IMenuItem
        {
            private readonly ToolStripItem m_menu;
            private readonly Action m_callback;

            public ToolStripItem MenuItem { get { return m_menu; } }
            
            public MenuItemWrapper(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
            {
                if (text == "-")
                    m_menu = new ToolStripSeparator();
                else
                {
                    m_menu = new ToolStripMenuItem(text);
                    if (icon != MenuIcons.None)
                        m_menu.Image = GetIcon(icon);
                    
                    if (callback != null)
                    {
                        m_callback = callback;
                        m_menu.Click += OnMenuClick;
                    }
                    
                    if (subitems != null && subitems.Count > 0)
                        foreach(var itm in subitems)
                            ((ToolStripMenuItem)m_menu).DropDownItems.Add(((MenuItemWrapper)itm).MenuItem);
                }
            }

            private void OnMenuClick (object sender, EventArgs e)
            {
                m_callback();
            }
            
            private System.Drawing.Image GetIcon(MenuIcons icon)
            {
                switch(icon)
                {
                case MenuIcons.Pause:
                    return ImageLoader.Pause;
                case MenuIcons.Quit:
                    return ImageLoader.CloseMenuIcon;
                case MenuIcons.Resume:
                    return ImageLoader.Play;
                case MenuIcons.Status:
                    return ImageLoader.StatusMenuIcon;
                case MenuIcons.None:
                default:
                    return null;
                }
            }

            #region IMenuItem implementation
            public void SetText(string text)
            {
                m_menu.Text = text;
            }

            public void SetIcon(MenuIcons icon)
            {
                m_menu.Image = GetIcon(icon);
            }

            public void SetDefault(bool value)
            {
                m_menu.Font = new System.Drawing.Font(m_menu.Font, System.Drawing.FontStyle.Bold);
            }
            #endregion
        }

        private Form m_handleProvider;
        private NotifyIcon m_trayIcon;

        public override void Init (string[] args)
        {
            //We need this ugly hack to get a handle that we can call Invoke on,
            // and sadly the TrayIcon does not expose one, and forcing the context menu
            // to create one causes weird "lost clicks"
            m_handleProvider = new Form()
            {
                FormBorderStyle = FormBorderStyle.None,
                Width = 10,
                Height = 10,
                Top = 0,
                Left = 0
            };
            m_handleProvider.Show();
            m_handleProvider.Hide();

            // Attempt to hide the form later, as it appears there is an issue with show/hide
            m_handleProvider.Shown += (_, _) => m_handleProvider.Hide();
            // Task.Delay(500).ContinueWith(_ => UpdateUIState(() => m_handleProvider.Hide()));

            m_trayIcon = new NotifyIcon();
            m_trayIcon.DoubleClick += new EventHandler(m_trayIcon_DoubleClick);
            m_trayIcon.Click += new EventHandler(m_trayIcon_Click);
            m_trayIcon.BalloonTipClicked += m_BalloonTipClicked;            
            m_trayIcon.Text = Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName;            
            base.Init(args);
        }
        
        protected override void UpdateUIState(Action action)
        {
            m_handleProvider.Invoke(action);
        }

        private void m_trayIcon_Click(object sender, EventArgs e)
        {
            if (m_onSingleClick != null)
                m_onSingleClick();
            else if (m_trayIcon != null && m_trayIcon.ContextMenuStrip != null)
            {
                // Show context menu on left-click as we have nothing happening otherwise
                try
                {
                    typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(m_trayIcon, null);
                }
                catch { }
            }
        }
        
        private void m_trayIcon_DoubleClick(object sender, EventArgs e)
        {
            if (m_onDoubleClick != null)
                m_onDoubleClick();
        }
               
        private void m_BalloonTipClicked(object sender, EventArgs e)
        {
            m_onNotificationClick?.Invoke();
        }

        protected override void RegisterStatusUpdateCallback ()
        {
            Program.Connection.OnStatusUpdated += delegate(IServerStatus status) {
                m_handleProvider.Invoke(new Action<IServerStatus>(OnStatusUpdated), status);
            };
        }
        
        #region implemented abstract members of Duplicati.GUI.TrayIcon.TrayIconBase
        protected override void Run (string[] args)
        {
            m_trayIcon.Visible = true;
            Application.Run();
        }
       
        protected override IMenuItem CreateMenuItem (string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            return new MenuItemWrapper(text, icon, callback, subitems);
        }

        protected override void NotifyUser(string title, string message, NotificationType type)
        {
            var icon = ToolTipIcon.None;

            switch (type)
            {
                case NotificationType.Information:
                    icon = ToolTipIcon.Info;
                    break;
                case NotificationType.Warning:
                    icon = ToolTipIcon.Warning;
                    break;
                case NotificationType.Error:
                    icon = ToolTipIcon.Error;
                    break;
            }

            m_trayIcon.ShowBalloonTip((int)TimeSpan.FromSeconds(60).TotalMilliseconds, title, message, icon);
        }

        protected override void Exit ()
        {
            Application.Exit();
        }

        protected override void SetIcon(TrayIcons icon)
        {
            switch (icon)
            {
                case TrayIcons.IdleError:
                    m_trayIcon.Icon = ImageLoader.LoadIcon(ImageLoader.ErrorIcon, System.Windows.Forms.SystemInformation.SmallIconSize);
                    break;
                case TrayIcons.IdleWarning:
                    m_trayIcon.Icon = ImageLoader.LoadIcon(ImageLoader.WarningIcon, System.Windows.Forms.SystemInformation.SmallIconSize);
                    break;
                case TrayIcons.Paused:
                    m_trayIcon.Icon = ImageLoader.LoadIcon(ImageLoader.PauseIcon, System.Windows.Forms.SystemInformation.SmallIconSize);
                    break;
                case TrayIcons.PausedError:
                    m_trayIcon.Icon = ImageLoader.LoadIcon(ImageLoader.PauseIcon, System.Windows.Forms.SystemInformation.SmallIconSize);
                    break;
                case TrayIcons.Running:
                    m_trayIcon.Icon = ImageLoader.LoadIcon(ImageLoader.WorkingIcon, System.Windows.Forms.SystemInformation.SmallIconSize);
                    break;
                case TrayIcons.RunningError:
                    m_trayIcon.Icon = ImageLoader.LoadIcon(ImageLoader.WorkingIcon, System.Windows.Forms.SystemInformation.SmallIconSize);
                    break;
                case TrayIcons.Idle:
                default:
                    m_trayIcon.Icon = ImageLoader.LoadIcon(ImageLoader.NormalIcon, System.Windows.Forms.SystemInformation.SmallIconSize);
                    break;
            }
        }

        protected override void SetMenu (System.Collections.Generic.IEnumerable<IMenuItem> items)
        {
            m_trayIcon.ContextMenuStrip = new ContextMenuStrip();
            foreach(var itm in items)
                m_trayIcon.ContextMenuStrip.Items.Add(((MenuItemWrapper)itm).MenuItem);
        }

        public override void Dispose ()
        {
            if (m_handleProvider != null)
            {
                m_handleProvider.Dispose();
                m_handleProvider = null;
            }

            if (m_trayIcon != null)
            {
                m_trayIcon.Visible = false;
                m_trayIcon.Dispose();
                m_trayIcon = null;
            }
        }
        #endregion

        /*public override IBrowserWindow ShowUrlInWindow(string url)
        {
            var v = new WindowsBrowser(url);
            v.Show();
            return v;
        }*/
    }
}

