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
using System.Collections.Generic;

namespace Duplicati.GUI.TrayIcon
{
    public enum MenuIcons
    {
        None,
        Wizard,
        Status,
        Quit,
        Options,
        Pause,
        Resume,
        Stop,
        Pause5,
        Pause15,
        Pause30,
        Pause60,
        Throttle
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
    }
    
    public abstract class TrayIconBase : IDisposable
    {           
        protected IMenuItem m_stopMenu;
        protected IMenuItem m_pauseMenu;
        protected bool m_stateIsPaused;
        
        public virtual void Init(string[] args)
        {
            SetMenu(BuildMenu());
            RegisterStatusUpdateCallback();
            OnStatusUpdated(Program.Connection.Status);
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
            return new IMenuItem[] {
                CreateMenuItem("Status", MenuIcons.Status, OnStatusClicked, null),
                CreateMenuItem("Wizard...", MenuIcons.Wizard, OnWizardClicked, null),
                CreateMenuItem("-",  MenuIcons.None, null, null),    
                CreateMenuItem("Options...", MenuIcons.Options, OnOptionsClicked, null),
                CreateMenuItem("-",  MenuIcons.None, null, null),    
                CreateMenuItem("Control", MenuIcons.None, null, new IMenuItem[] {
                    m_pauseMenu = CreateMenuItem("Pause", MenuIcons.Pause, OnPauseClicked, null ),
                    CreateMenuItem("Pause period", MenuIcons.None, null, new IMenuItem[] {
                        CreateMenuItem("5 minutes", MenuIcons.Pause5, OnPause5Clicked, null),
                        CreateMenuItem("15 minutes", MenuIcons.Pause15, OnPause15Clicked, null),
                        CreateMenuItem("30 minutes", MenuIcons.Pause30, OnPause30Clicked, null),
                        CreateMenuItem("60 minutes", MenuIcons.Pause60, OnPause60Clicked, null)
                    }),
                    m_stopMenu = CreateMenuItem("Stop", MenuIcons.Stop, OnStopClicked, null),
                    CreateMenuItem("Throttle options", MenuIcons.Throttle, OnThrottleClicked, null),
                }),
                CreateMenuItem("-",  MenuIcons.None, null, null),    
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
        
        protected void OnPause5Clicked()
        {
            Program.Connection.Pause("5m");
        }

        protected void OnPause15Clicked()
        {
            Program.Connection.Pause("15m");
        }

        protected void OnPause30Clicked()
        {
            Program.Connection.Pause("30m");
        }

        protected void OnPause60Clicked()
        {
            Program.Connection.Pause("60m");
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
            
            m_stopMenu.Enabled = status.ActiveScheduleId >= 0;
            
        }

        #region IDisposable implementation
        public abstract void Dispose();
        #endregion
    }
}

