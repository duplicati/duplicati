// Copyright (C) 2025, The Duplicati Team
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
        IdleWarning,
        IdleError,
        PausedError,
        RunningError,
        Disconnected
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
        void SetDefault(bool isDefault);
        void SetIcon(MenuIcons icon);
        void SetText(string text);
        void SetEnabled(bool enabled);
        void SetHidden(bool hidden);
    }

    public abstract class TrayIconBase : IDisposable
    {
        protected IMenuItem m_reconnectMenu;
        protected IMenuItem m_openMenu;
        protected IMenuItem m_pauseMenu;
        protected IMenuItem m_quitMenu;
        protected bool m_stateIsPaused;
        protected Action m_onSingleClick;
        protected Action m_onDoubleClick;
        protected Action m_onNotificationClick;

        public virtual void Init(string[] args)
        {
            SetMenu(BuildMenu());
            RegisterStatusUpdateCallback();
            RegisterNotificationCallback();
            m_onDoubleClick = ShowStatusWindow;
            m_onNotificationClick = ShowStatusWindow;
            OnStatusUpdated(Program.Connection.Status);
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

        public abstract void NotifyUser(string title, string message, NotificationType type);

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

            UpdateUIState(() =>
            {
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
            m_reconnectMenu = CreateMenuItem("Reconnect", MenuIcons.Resume, Reconnect, null);
            m_reconnectMenu.SetHidden(true);
            m_openMenu = CreateMenuItem("Open", MenuIcons.Status, OnStatusClicked, null);
            m_openMenu.SetDefault(true);
            return new IMenuItem[] {
                m_reconnectMenu,
                m_openMenu,
                m_pauseMenu = CreateMenuItem("Pause", MenuIcons.Pause, OnPauseClicked, null ),
                m_quitMenu = CreateMenuItem("Quit", MenuIcons.Quit, OnQuitClicked, null),
            };
        }

        private void Reconnect()
        {
            Program.Connection.UpdateStatus().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Console.WriteLine("Failed to reconnect: " + t.Exception.Message);
                    NotifyUser("Failed to reconnect", t.Exception.Message, NotificationType.Error);
                }

            });
        }

        protected void ShowStatusWindow()
        {
            Program.Connection.GetStatusWindowURLAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Console.WriteLine("Failed to get status window URL: " + t.Exception.Message);
                    NotifyUser("Failed to get status window URL", t.Exception.Message, NotificationType.Error);
                    return;
                }

                var window = ShowUrlInWindow(t.Result);
                if (window != null)
                {
                    window.SetIcon(WindowIcons.Regular);
                    window.SetTitle("Duplicati status");
                }
            });
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
            this.UpdateUIState(() =>
            {
                switch (status.SuggestedStatusIcon)
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
                        this.SetIcon(TrayIcons.IdleWarning);
                        break;
                    case SuggestedStatusIcon.Paused:
                        this.SetIcon(TrayIcons.Paused);
                        break;
                    case SuggestedStatusIcon.Disconnected:
                        this.SetIcon(TrayIcons.Disconnected);
                        break;
                    case SuggestedStatusIcon.Ready:
                    default:
                        this.SetIcon(TrayIcons.Idle);
                        break;

                }

                if (status.ProgramState == LiveControlState.Running)
                {
                    m_pauseMenu.SetIcon(MenuIcons.Pause);
                    m_pauseMenu.SetText("Pause");
                    m_stateIsPaused = false;
                }
                else
                {
                    m_pauseMenu.SetIcon(MenuIcons.Resume);
                    m_pauseMenu.SetText("Resume");
                    m_stateIsPaused = true;
                }

                m_openMenu.SetHidden(status.SuggestedStatusIcon == SuggestedStatusIcon.Disconnected);
                m_pauseMenu.SetHidden(status.SuggestedStatusIcon == SuggestedStatusIcon.Disconnected);
                m_reconnectMenu.SetHidden(status.SuggestedStatusIcon != SuggestedStatusIcon.Disconnected);
            });
        }

        #region IDisposable implementation
        public abstract void Dispose();
        #endregion
    }
}

