//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
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
using System.Runtime.InteropServices;
using System.Drawing;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.GUI.TrayIcon
{
    public class Win32Runner : TrayIconBase
    {
        private const string WINDOW_CLASSNAME = "DuplicatiMessageMonitorWindowTrayIcon";
        private const int TRAY_ICON_ID_1 = 1;
        public const int WM_TRAYICON_1 = Win32NativeNotifyIcon.WM_APP + 1;
        private readonly Win32Window m_window = new Win32Window();
        private Win32NotifyIcon m_ntfIcon;
        private bool m_TrayIconCreated = false;
        List<Win32MenuItem> m_TrayContextMenu;

        public override void Init(string[] args)
        {
            base.Init(args);

            m_window.MessageReceived += MessageMonitor_MessageReceived;
            m_window.CreateWindow(WINDOW_CLASSNAME);
        }

        private void MessageMonitor_MessageReceived(IntPtr hwnd, Win32NativeWindow.WindowsMessage message, IntPtr wParam, IntPtr lParam)
        {
            switch (message)
            {
                case Win32NativeWindow.WindowsMessage.WM_SHOWWINDOW:
                    if (!m_TrayIconCreated)
                    {
                        m_ntfIcon = new Win32NotifyIcon(m_window.Handle, TRAY_ICON_ID_1, WM_TRAYICON_1,
                            Win32IconLoader.TrayNormalIcon, Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName);
                        m_ntfIcon.Create();
                        m_TrayIconCreated = true;
                    }
                    break;
                case (Win32NativeWindow.WindowsMessage)WM_TRAYICON_1:
                    var llpTray = (Win32NativeWindow.WindowsMessage)((uint)lParam.ToInt32() & 0x0000FFFF);

                    switch (llpTray)
                    {
                        case Win32NativeWindow.WindowsMessage.WM_LBUTTONDBLCLK:
                            m_onDoubleClick?.Invoke();
                            break;
                        case Win32NativeWindow.WindowsMessage.WM_LBUTTONUP:
                            m_onSingleClick?.Invoke();
                            break;
                        case Win32NativeWindow.WindowsMessage.WM_CONTEXTMENU:
                            Win32NativeMenu.ShowContextMenu(m_window.Handle, m_TrayContextMenu)?.Callback?.Invoke();
                            break;
                        case Win32NativeWindow.WindowsMessage.NIN_BALLOONUSERCLICK:
                            m_onNotificationClick?.Invoke();
                            break;
                    }
                    break;
            }
        }

        protected override void UpdateUIState(Action action)
        {
            action.Invoke();
        }

        protected override void RegisterStatusUpdateCallback()
        {
            Program.Connection.OnStatusUpdated += delegate (IServerStatus status)
            {
                this.OnStatusUpdated(status);
            };
        }

        #region implemented abstract members of Duplicati.GUI.TrayIcon.TrayIconBase
        protected override void Run(string[] args)
        {
        }

        protected override IMenuItem CreateMenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            return new Win32MenuItem(text, icon, callback, subitems);
        }

        protected override void NotifyUser(string title, string message, NotificationType type)
        {
            var icon = Win32NativeNotifyIcon.InfoFlags.NIIF_INFO;

            switch (type)
            {
                case NotificationType.Information:
                    icon = Win32NativeNotifyIcon.InfoFlags.NIIF_INFO;
                    break;
                case NotificationType.Warning:
                    icon = Win32NativeNotifyIcon.InfoFlags.NIIF_WARNING;
                    break;
                case NotificationType.Error:
                    icon = Win32NativeNotifyIcon.InfoFlags.NIIF_ERROR;
                    break;
            }

            m_ntfIcon.ShowBalloonTip(title, message, icon);
        }

        protected override void Exit()
        {
            m_ntfIcon.Delete();
            m_window.DestroyWindow();
        }

        protected override void SetIcon(TrayIcons icon)
        {
            //There are calls before NotifyIcons is created
            if (m_ntfIcon == null)
                return;

            switch (icon)
            {
                case TrayIcons.IdleError:
                    m_ntfIcon?.SetIcon(Win32IconLoader.TrayErrorIcon);
                    break;
                case TrayIcons.Paused:
                case TrayIcons.PausedError:
                    m_ntfIcon?.SetIcon(Win32IconLoader.TrayPauseIcon);
                    break;
                case TrayIcons.Running:
                case TrayIcons.RunningError:
                    m_ntfIcon?.SetIcon(Win32IconLoader.TrayWorkingIcon);
                    break;
                case TrayIcons.Idle:
                default:
                    m_ntfIcon?.SetIcon(Win32IconLoader.TrayNormalIcon);
                    break;
            }
        }

        protected override void SetMenu(System.Collections.Generic.IEnumerable<IMenuItem> items)
        {
            m_TrayContextMenu = new List<Win32MenuItem>();

            foreach(var item in items)
                m_TrayContextMenu.Add((Win32MenuItem)item);
        }

        public override void Dispose()
        {
        }
        #endregion
    }
}
