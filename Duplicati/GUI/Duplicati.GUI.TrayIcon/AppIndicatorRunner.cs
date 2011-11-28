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
using AppIndicator;
using Gtk;
using Duplicati.Server.Serialization;
using System.Collections.Generic;

namespace Duplicati.GUI.TrayIcon
{
    public class AppIndicatorRunner : GtkRunner
    {
        protected ApplicationIndicator m_appIndicator;
        private string m_themeFolder;
                
        protected override void CreateTrayInstance()
        {
            m_themeFolder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SVGIcons");
            m_themeFolder = System.IO.Path.Combine(m_themeFolder, "dark");
            
            m_appIndicator = new ApplicationIndicator("duplicati", "duplicati-logo", Category.ApplicationStatus, m_themeFolder);
        }
        
        protected override void Run(string[] args)
        {
            m_appIndicator.Menu.ShowAll(); 
            m_appIndicator.Status = Status.Attention;
            Gtk.Application.Run();
        }
        
        
        protected override void SetMenu (IEnumerable<IMenuItem> items)
        {
            base.SetMenu(items);
            m_appIndicator.Menu = m_popupMenu;
        }
        
        protected override TrayIcons Icon 
        {
            set 
            {
                switch(value)
                {
                case TrayIcons.Idle:
                default:
                    m_appIndicator.IconName = "duplicati-logo";
                    break;
                }
            }
        }

    }
}

