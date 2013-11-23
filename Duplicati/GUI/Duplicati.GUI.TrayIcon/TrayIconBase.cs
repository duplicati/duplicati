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
            OnStatusUpdated(Program.Connection.Status);
            m_onDoubleClick = ShowStatusWindow;
            Run(args);
        }
        
        protected abstract void Run(string[] args);
            
        
        protected abstract TrayIcons Icon { set; }
        
        protected abstract IMenuItem CreateMenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems);
        
        protected abstract void Exit();
        
        protected abstract void SetMenu(IEnumerable<IMenuItem> items);
        
        protected virtual void RegisterStatusUpdateCallback()
        {
            Program.Connection.StatusUpdated += OnStatusUpdated;
        }

        public virtual IBrowserWindow ShowUrlInWindow(string url)
        {
            //Fallback is to just show the window in a browser
            if (Duplicati.Library.Utility.Utility.IsClientLinux)
                try { System.Diagnostics.Process.Start("open", "\"" + Program.Connection.StatusWindowURL + "\""); }
                catch { }
            else
                try { System.Diagnostics.Process.Start("\"" + Program.Connection.StatusWindowURL + "\""); }
                catch { }

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

        protected void OnWizardClicked()
        {
        }
        
        protected void OnOptionsClicked()
        {
        }
        
        protected void OnStopClicked()
        {
        }

        protected void OnQuitClicked()
        {
            Exit();
        }

        protected void OnThrottleClicked()
        {
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
            switch(status.SuggestedStatusIcon)
            {
                case SuggestedStatusIcon.Active:
                    Icon = TrayIcons.Running;
                    break;
                case SuggestedStatusIcon.ActivePaused:
                    Icon = TrayIcons.Paused;
                    break;
                case SuggestedStatusIcon.ReadyError:
                    Icon = TrayIcons.IdleError;
                    break;
                case SuggestedStatusIcon.ReadyWarning:
                    Icon = TrayIcons.IdleError;
                    break;
                case SuggestedStatusIcon.Paused:
                    Icon = TrayIcons.Paused;
                    break;
                case SuggestedStatusIcon.Ready:
                default:    
                    Icon = TrayIcons.Idle;
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
            
        }

        #region IDisposable implementation
        public abstract void Dispose();
        #endregion
    }
}

