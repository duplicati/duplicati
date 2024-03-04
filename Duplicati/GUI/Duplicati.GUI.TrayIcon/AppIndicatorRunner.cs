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

#if __WindowsGTK__ || ENABLE_GTK
using System;
using AppIndicator;
using Gtk;
using System.Collections.Generic;

using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;
using Duplicati.Server.Serialization;


namespace Duplicati.GUI.TrayIcon
{
    public class AppIndicatorRunner : GtkRunner
    {
        protected ApplicationIndicator m_appIndicator;
        private string m_themeFolder;
                
        protected override void CreateTrayInstance()
        {
            m_themeFolder = SystemIO.IO_OS.PathCombine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SVGIcons");
            m_themeFolder = SystemIO.IO_OS.PathCombine(m_themeFolder, "dark");
            
            m_appIndicator = new ApplicationIndicator("duplicati", "normal", Category.ApplicationStatus, m_themeFolder);
        }
        
        protected override void Run(string[] args)
        {
            m_appIndicator.Menu.ShowAll(); 
            m_appIndicator.Status = Status.Active;
            Gtk.Application.Run();
        }
        
        protected override void SetMenu (IEnumerable<IMenuItem> items)
        {
            base.SetMenu(items);
            m_appIndicator.Menu = m_popupMenu;
        }
        
        protected override void SetIcon(TrayIcons icon)
        {
            m_appIndicator.IconName = GetTrayIconFilename(icon);

            switch (icon)
            {
                case TrayIcons.Paused:
                    m_appIndicator.IconDesc = "Paused";
                    break;
                case TrayIcons.Running:
                    m_appIndicator.IconDesc = "Running";
                    break;
                case TrayIcons.IdleWarning:
                    m_appIndicator.IconDesc = "Warning";
                    break;
                case TrayIcons.IdleError:
                    m_appIndicator.IconDesc = "Error";
                    break;
                case TrayIcons.RunningError:
                    m_appIndicator.IconDesc = "Running";
                    break;
                case TrayIcons.PausedError:
                    m_appIndicator.IconDesc = "Paused";
                    break;
                case TrayIcons.Idle:
                default:
                    m_appIndicator.IconDesc = "Ready";
                    break;
            }
        }
    }
}
#endif
