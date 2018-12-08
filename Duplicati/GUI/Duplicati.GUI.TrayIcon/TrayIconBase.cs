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
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using System.Collections.Generic;

namespace Duplicati.GUI.TrayIcon
{
    public enum MenuIcons
    {
        None,
        Status,
        Quit,
        Pause,
        Resume,
    }
    
    public enum TrayIcons
    {
        Idle,
        Paused,
        Running,
        IdleError,
        PausedError,
        RunningError
    }

    public enum WindowIcons
    {
        Regular,
        LogWindow
    }

    public enum NotificationType
    {
        Information,
        Warning,
        Error
    }
    
    public interface IMenuItem
    {
        string Text { set; }
        MenuIcons Icon { set; }
        bool Enabled { set; }
        bool Default { set; }
    }
    
    public abstract class TrayIconBase : IDisposable
    {           
        protected IMenuItem m_pauseMenu;
        protected bool m_stateIsPaused;
        protected Action m_onSingleClick;
        protected Action m_onDoubleClick;
        
        public virtual void Init(string[] args)
        {
            SetMenu(BuildMenu());
            RegisterStatusUpdateCallback();
            RegisterNotificationCallback();
            OnStatusUpdated(Program.Connection.Status);
            m_onDoubleClick = ShowStatusWindow;
            Run(args);
        }
        
        protected abstract void Run(string[] args);

        public void InvokeExit()
        {
            UpdateUIState(() => { this.Exit(); });
        }
        
        protected virtual void UpdateUIState(Action action)
        {
            action();
        }
        
        protected abstract IMenuItem CreateMenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems);
        
        protected abstract void Exit();

        protected abstract void SetIcon(TrayIcons icon);
        
        protected abstract void SetMenu(IEnumerable<IMenuItem> items);

        protected abstract void NotifyUser(string title, string message, NotificationType type);

        protected virtual void RegisterStatusUpdateCallback()
        {
            Program.Connection.OnStatusUpdated += OnStatusUpdated;
        }

        protected virtual void RegisterNotificationCallback()
        {
            Program.Connection.OnNotification += OnNotification;
        }

        protected void OnNotification(INotification notification)
        {
            var type = NotificationType.Information;
            switch (notification.Type)
            {
                case Server.Serialization.NotificationType.Information:
                    type = NotificationType.Information;
                    break;
                case Server.Serialization.NotificationType.Warning:
                    type = NotificationType.Warning;
                    break;
                case Server.Serialization.NotificationType.Error:
                    type = NotificationType.Error;
                    break;
            }

            UpdateUIState(() => {
                NotifyUser(notification.Title, notification.Message, type);
            });
        }

        public virtual IBrowserWindow ShowUrlInWindow(string url)
        {
            //Fallback is to just show the window in a browser
            Duplicati.Library.Utility.UrlUtility.OpenURL(url, Program.BrowserCommand);

            return null;
        }

        protected IEnumerable<IMenuItem> BuildMenu() 
        {
            var tmp = CreateMenuItem("Open", MenuIcons.Status, OnStatusClicked, null);
            tmp.Default = true;
            return new IMenuItem[] {
                tmp,
                m_pauseMenu = CreateMenuItem("Pause", MenuIcons.Pause, OnPauseClicked, null ),
                CreateMenuItem("Quit", MenuIcons.Quit, OnQuitClicked, null),
            };
        }
        
        protected void ShowStatusWindow()
        {
            var window = ShowUrlInWindow(Program.Connection.StatusWindowURL);
            if (window != null)
            {
                window.Icon = WindowIcons.Regular;
                window.Title = "Duplicati status";
            }
        }

        protected void OnStatusClicked()
        {
            ShowStatusWindow();
        }

        protected void OnQuitClicked()
        {
            Exit();
        }

        protected void OnPauseClicked()
        {
            if (m_stateIsPaused)
                Program.Connection.Resume();
            else
                Program.Connection.Pause();
        }
        
        protected void OnStatusUpdated(IServerStatus status)
        {
            this.UpdateUIState(() => {
                switch(status.SuggestedStatusIcon)
                {
                    case SuggestedStatusIcon.Active:
                        this.SetIcon(TrayIcons.Running);
                        break;
                    case SuggestedStatusIcon.ActivePaused:
                        this.SetIcon(TrayIcons.Paused);
                        break;
                    case SuggestedStatusIcon.ReadyError:
                        this.SetIcon(TrayIcons.IdleError);
                        break;
                    case SuggestedStatusIcon.ReadyWarning:
                        this.SetIcon(TrayIcons.IdleError);
                        break;
                    case SuggestedStatusIcon.Paused:
                        this.SetIcon(TrayIcons.Paused);
                        break;
                    case SuggestedStatusIcon.Ready:
                    default:    
                        this.SetIcon(TrayIcons.Idle);
                        break;
                    
                }
    
                if (status.ProgramState == LiveControlState.Running)
                {
                    m_pauseMenu.Icon = MenuIcons.Pause;
                    m_pauseMenu.Text = "Pause";
                    m_stateIsPaused = false;
                }
                else
                {
                    m_pauseMenu.Icon = MenuIcons.Resume;
                    m_pauseMenu.Text = "Resume";
                    m_stateIsPaused = true;
                }
            });
        }

        #region IDisposable implementation
        public abstract void Dispose();
        #endregion
    }
}

