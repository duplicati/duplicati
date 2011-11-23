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
using Gtk;
using Gdk;
using Duplicati.Server.Serialization;

namespace Duplicati.GUI.TrayIcon
{
    //We use a separate class to start the runner to avoid attempts to load Gtk from Program.cs
    public class GtkRunner
    {
        /// <summary>
        /// Static constructor that ensures the Gtk environment is initialized
        /// </summary>
        static GtkRunner()
        {
            Gtk.Application.Init();
        }
        
        /// <summary>
        /// Container class for passing the status as an event argument
        /// </summary>
        private class StatusEvent : EventArgs
        {
            public ISerializableStatus Status;
            public StatusEvent(ISerializableStatus status) { this.Status = status; }
        }
  
        /// <summary>
        /// Creates a new Image menu with a single calls
        /// </summary>
        /// <returns>
        /// The image menu.
        /// </returns>
        /// <param name='label'>The menu text</param>
        /// <param name='subitems'>Any submenu items</param>
        private static ImageMenuItem NewImageMenu(string label, params MenuItem[] subitems)
        {
            return NewImageMenu(label, (Gtk.Image)null, null, subitems);
        }
        
        /// <summary>
        /// Creates a new Image menu with a single calls
        /// </summary>
        /// <returns>
        /// The image menu.
        /// </returns>
        /// <param name='label'>The menu text</param>
        /// <param name='img'>The image to assing the menu or null</param>
        /// <param name='clickhandler'>A handler to execute on click</param>
        /// <param name='subitems'>Any submenu items</param>
        private static ImageMenuItem NewImageMenu(string label, System.Drawing.Image img, EventHandler clickhandler = null, params MenuItem[] subitems)
        {
            return NewImageMenu(label, ImageToGtk(img), clickhandler, subitems);
        }

        /// <summary>
        /// Creates a new Image menu with a single calls
        /// </summary>
        /// <returns>
        /// The image menu.
        /// </returns>
        /// <param name='label'>The menu text</param>
        /// <param name='img'>The image to assing the menu or null</param>
        /// <param name='clickhandler'>A handler to execute on click</param>
        /// <param name='subitems'>Any submenu items</param>
        private static ImageMenuItem NewImageMenu(string label, Gtk.Image img, EventHandler clickhandler = null, params MenuItem[] subitems)
        {
            ImageMenuItem m = new ImageMenuItem(label);
            if (img != null)
                m.Image = img;
            
            if (subitems != null && subitems.Length > 0)
            {
                Menu s = new Menu();
                foreach(var sm in subitems)
                    s.Add(sm);
                
                m.Submenu = s;
            }
            
            if (clickhandler != null)
                m.Activated += clickhandler;
            
            return m;
        }
                
        
        private readonly Gdk.Pixbuf NORMAL_ICON = ImageToPixbuf(Properties.Resources.TrayNormal.ToBitmap());
        private readonly Gdk.Pixbuf NORMAL_PAUSED_ICON = ImageToPixbuf(Properties.Resources.TrayNormalPause.ToBitmap());
        private readonly Gdk.Pixbuf NORMAL_WARNING_ICON = ImageToPixbuf(Properties.Resources.TrayNormalWarning.ToBitmap());
        private readonly Gdk.Pixbuf NORMAL_ERROR_ICON = ImageToPixbuf(Properties.Resources.TrayNormalError.ToBitmap());
        private readonly Gdk.Pixbuf WORKING_ICON = ImageToPixbuf(Properties.Resources.TrayWorking.ToBitmap());
        private readonly Gdk.Pixbuf WORKING_PAUSED_ICON = ImageToPixbuf(Properties.Resources.TrayWorkingPause.ToBitmap());
        
        private readonly Gtk.Image MENU_PAUSE_IMAGE = ImageToGtk(Properties.Resources.Pause);
        private readonly Gtk.Image MENU_RESUME_IMAGE = ImageToGtk(Properties.Resources.Play);
        
        private Menu m_popupMenu;
        private bool m_stateIsPaused = false;        
        
        private Gtk.StatusIcon trayIcon;
        private ImageMenuItem pauseMenuItem;
        private MenuItem stopMenuItem;
        
        public void RunMain()
        {
            trayIcon = new Gtk.StatusIcon(NORMAL_ICON);

            trayIcon.Visible = true;
            trayIcon.PopupMenu += OnTrayIconPopup;
   
            m_popupMenu = new Menu();
            
            //TODO: Translation
                        
            var menuitems = new MenuItem[] {
                NewImageMenu("Status", Properties.Resources.StatusMenuIcon),
                NewImageMenu("Wizard...", Properties.Resources.WizardMenuIcon),
                new SeparatorMenuItem(),    
                NewImageMenu("Options...", Properties.Resources.SettingsMenuIcon),
                new SeparatorMenuItem(),
                NewImageMenu("Control", new MenuItem[] {
                    pauseMenuItem = (ImageMenuItem)NewImageMenu(Strings.WindowsMainForm.PauseMenuText, MENU_PAUSE_IMAGE, delegate (object sender, EventArgs args) { if (m_stateIsPaused) { Program.Connection.Resume(); } else { Program.Connection.Pause(); } } ),
                    NewImageMenu("Pause period", new MenuItem[] {
                        NewImageMenu("5 minutes", Properties.Resources.Clock05, delegate (object sender, EventArgs args) { Program.Connection.Pause("5m"); }),
                        NewImageMenu("15 minutes", Properties.Resources.Clock15, delegate (object sender, EventArgs args) { Program.Connection.Pause("15m"); }),
                        NewImageMenu("30 minutes", Properties.Resources.Clock30, delegate (object sender, EventArgs args) { Program.Connection.Pause("30m"); }),
                        NewImageMenu("60 minutes", Properties.Resources.Clock60, delegate (object sender, EventArgs args) { Program.Connection.Pause("60m"); })
                    }),
                    stopMenuItem = NewImageMenu("Stop", Properties.Resources.Stop, delegate (object sender, EventArgs args) { Program.Connection.StopBackup(); }),
                    NewImageMenu("Throttle options", Properties.Resources.Throttle),
                }),
                new SeparatorMenuItem(),
                NewImageMenu("Quit", new Gtk.Image(Stock.Quit, IconSize.Menu), delegate (object sender, EventArgs args) { Gtk.Application.Quit(); }),
            };
            
            stopMenuItem.Sensitive = false;
            
            foreach(var m in menuitems)
                m_popupMenu.Add(m);
            
            Program.Connection.StatusUpdated += delegate(ISerializableStatus status) {
                Gtk.Application.Invoke(Program.Connection, new StatusEvent(status), Status_Updated_EventHandler);         
            };
            
            Status_Updated_EventHandler(Program.Connection, new StatusEvent(Program.Connection.Status));
            
            Gtk.Application.Run();
            
        }        
                
        private void Status_Updated_EventHandler(object sender, EventArgs e)
        {
            if (e as StatusEvent == null)
                return;
            
            ISerializableStatus status = ((StatusEvent)e).Status;

            switch(status.SuggestedStatusIcon)
            {
                case SuggestedStatusIcon.Active:
                    trayIcon.Pixbuf = WORKING_ICON;
                    break;
                case SuggestedStatusIcon.ActivePaused:
                    trayIcon.Pixbuf =  WORKING_PAUSED_ICON;
                    break;
                case SuggestedStatusIcon.ReadyError:
                    trayIcon.Pixbuf =  NORMAL_ERROR_ICON;
                    break;
                case SuggestedStatusIcon.ReadyWarning:
                    trayIcon.Pixbuf =  NORMAL_WARNING_ICON;
                    break;
                case SuggestedStatusIcon.Paused:
                    trayIcon.Pixbuf =  NORMAL_PAUSED_ICON;
                    break;
                case SuggestedStatusIcon.Ready:
                default:    
                    trayIcon.Pixbuf = NORMAL_ICON;
                    break;
                
            }

            if (status.ProgramState == LiveControlState.Running)
            {
                pauseMenuItem.Image = MENU_PAUSE_IMAGE;
                ((Gtk.Label)pauseMenuItem.Child).Text = Strings.WindowsMainForm.PauseMenuText;
                m_stateIsPaused = false;
            }
            else
            {
                pauseMenuItem.Image = MENU_RESUME_IMAGE;
                ((Gtk.Label)pauseMenuItem.Child).Text = Strings.WindowsMainForm.ResumeMenuText;
                m_stateIsPaused = true;
            }
            
            stopMenuItem.Sensitive = status.ActiveScheduleId >= 0;
        }
        
        
        private void OnTrayIconPopup (object o, EventArgs args) 
        {
            m_popupMenu.ShowAll();
            m_popupMenu.Popup(null, null, null, 0u, 0u);
        }
        
        private static Gdk.Pixbuf ImageToPixbuf(System.Drawing.Image image)
        {
            using (var stream = new System.IO.MemoryStream()) 
            {
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                Gdk.Pixbuf pixbuf = new Gdk.Pixbuf(stream);
                return pixbuf;
            }
        }    

        private static Gtk.Image ImageToGtk(System.Drawing.Image image)
        {
            using (var stream = new System.IO.MemoryStream()) 
            {
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                Gtk.Image img = new Gtk.Image(stream);
                return img;
            }
        }    
    }
}

