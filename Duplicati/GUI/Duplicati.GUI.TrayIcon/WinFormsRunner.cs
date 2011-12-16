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

namespace Duplicati.GUI.TrayIcon
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
                case MenuIcons.Options:
                    return Properties.Resources.SettingsMenuIcon;
                case MenuIcons.Pause:
                    return Properties.Resources.Pause;
                case MenuIcons.Pause5:
                    return Properties.Resources.Clock05;
                case MenuIcons.Pause15:
                    return Properties.Resources.Clock15;
                case MenuIcons.Pause30:
                    return Properties.Resources.Clock30;
                case MenuIcons.Pause60:
                    return Properties.Resources.Clock60;
                case MenuIcons.Quit:
                    return Properties.Resources.CloseMenuIcon;
                case MenuIcons.Resume:
                    return Properties.Resources.Play;
                case MenuIcons.Status:
                    return Properties.Resources.StatusMenuIcon;
                case MenuIcons.Stop:
                    return Properties.Resources.Stop;
                case MenuIcons.Throttle:
                    return Properties.Resources.Throttle;
                case MenuIcons.Wizard:
                    return Properties.Resources.WizardMenuIcon;
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
            #endregion
        }
        
        private NotifyIcon m_trayIcon;
        
        public override void Init (string[] args)
        {
            m_trayIcon = new NotifyIcon();
            base.Init (args);
        }
        
        protected override void RegisterStatusUpdateCallback ()
        {
            Program.Connection.StatusUpdated += delegate(ISerializableStatus status) {
                m_trayIcon.ContextMenuStrip.Invoke(new Action<ISerializableStatus>(OnStatusUpdated), status);
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
        }
        #endregion
    }
}

