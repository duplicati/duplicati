//  Copyright (C) 2011, Kenneth Skovhede
//  http://www.hexad.dk, opensource@hexad.dk
//  
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
// 
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
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
            private ToolStripItem m_menu;
            private Action m_callback;

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
                    return Properties.Resources.Pause;
                case MenuIcons.Quit:
                    return Properties.Resources.CloseMenuIcon;
                case MenuIcons.Resume:
                    return Properties.Resources.Play;
                case MenuIcons.Status:
                    return Properties.Resources.StatusMenuIcon;
                case MenuIcons.None:
                default:
                    return null;
                }
            }

            #region IMenuItem implementation
            public string Text {
                set {
                    m_menu.Text = value;
                }
            }

            public MenuIcons Icon {
                set {
                    m_menu.Image = GetIcon(value);
                }
            }

            public bool Enabled {
                set {
                    m_menu.Enabled = value;
                }
            }

            public bool Default {
                set {
                    m_menu.Font = new System.Drawing.Font(m_menu.Font, System.Drawing.FontStyle.Bold);
                }
            }
            #endregion
        }

        private Form m_handleProvider;
        private NotifyIcon m_trayIcon;

        public static WinFormsRunner Instance = null;
        
        public override void Init (string[] args)
        {
            if (WinFormsRunner.Instance != null)
                throw new Exception("Multiple trayicon instances not allowed!");

            WinFormsRunner.Instance = this;

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

            m_trayIcon = new NotifyIcon();
            m_trayIcon.DoubleClick += new EventHandler(m_trayIcon_DoubleClick);
            m_trayIcon.Click += new EventHandler(m_trayIcon_Click);
            base.Init(args);
        }

        private void m_trayIcon_Click(object sender, EventArgs e)
        {
            if (m_onSingleClick != null)
                m_onSingleClick();
        }
        
        private void m_trayIcon_DoubleClick(object sender, EventArgs e)
        {
            if (m_onDoubleClick != null)
                m_onDoubleClick();
        }
        
        protected override void RegisterStatusUpdateCallback ()
        {
            Program.Connection.StatusUpdated += delegate(IServerStatus status) {
                m_handleProvider.Invoke(new Action<IServerStatus>(OnStatusUpdated), status);
            };
        }
        
        #region implemented abstract members of Duplicati.GUI.TrayIcon.TrayIconBase
        protected override void Run (string[] args)
        {
            m_trayIcon.Visible = true;
            Application.Run();
        }

        protected override TrayIcons Icon {
            set {
                switch (value)
                {
                    case TrayIcons.IdleError:
                        m_trayIcon.Icon = Properties.Resources.TrayNormalError;
                        break;
                    case TrayIcons.Paused:
                        m_trayIcon.Icon = Properties.Resources.TrayNormalPause;
                        break;
                    case TrayIcons.PausedError:
                        m_trayIcon.Icon = Properties.Resources.TrayNormalPause;
                        break;
                    case TrayIcons.Running:
                        m_trayIcon.Icon = Properties.Resources.TrayWorking;
                        break;
                    case TrayIcons.RunningError:
                        m_trayIcon.Icon = Properties.Resources.TrayWorking;
                        break;
                    case TrayIcons.Idle:
                    default:
                        m_trayIcon.Icon = Properties.Resources.TrayNormal;
                        break;
                }
            }
        }

        protected override IMenuItem CreateMenuItem (string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            return new MenuItemWrapper(text, icon, callback, subitems);
        }

        protected override void Exit ()
        {
            Application.Exit();
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

