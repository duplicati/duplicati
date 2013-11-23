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
using Gdk;
using Gtk;

namespace Duplicati.GUI.TrayIcon
{
    public class GtkRunner : TrayIconBase
    {
        /// <summary>
        /// Static constructor that ensures the Gtk environment is initialized
        /// </summary>
        static GtkRunner()
        {
            Gtk.Application.Init();
        }
        
        private class StatusEventArgs : EventArgs
        {
            public readonly Duplicati.Server.Serialization.Interface.IServerStatus Status;
            public StatusEventArgs(Duplicati.Server.Serialization.Interface.IServerStatus args) { this.Status = args; }
        }
        
        private class MenuItemWrapper : IMenuItem
        {
            private MenuItem m_item;
            private System.Action m_callback;
            private static Dictionary<MenuIcons, Gtk.Image> _icons = new Dictionary<MenuIcons, Gtk.Image>();
            
            public MenuItem MenuItem { get { return m_item; } }
            
            private Gtk.Image GetIcon(MenuIcons icon)
            {
                if (!_icons.ContainsKey(icon))
                {
                    switch(icon)
                    {
                    case MenuIcons.None:
                        _icons[icon] = null;
                        break;
                    case MenuIcons.Pause:
                        _icons[icon] = ImageToGtk(Properties.Resources.Pause);
                        break;
                    case MenuIcons.Quit:
                        _icons[icon] = ImageToGtk(Properties.Resources.CloseMenuIcon);
                        break;
                    case MenuIcons.Resume:
                        _icons[icon] = ImageToGtk(Properties.Resources.Play);
                        break;
                    case MenuIcons.Status:
                        _icons[icon] = ImageToGtk(Properties.Resources.StatusMenuIcon);
                        break;
                    default:
                        _icons[icon] = null;
                        break;
                    }
                }
                
                return _icons[icon];
            }

            public MenuItemWrapper(string text, MenuIcons icon, System.Action callback, IList<IMenuItem> subitems)
            {
                if (text == "-")
                    m_item = new SeparatorMenuItem();
                else
                {
                    m_item = new ImageMenuItem(text);
                    if (icon != MenuIcons.None) {
                        ((ImageMenuItem)m_item).Image = GetIcon(icon);
                        
                        //TODO: Not sure we should do this, it overrides policy?
                        m_item.ExposeEvent += DrawImageMenuItemImage;
                    }
                    
                    if (subitems != null && subitems.Count > 0)
                    {
                        Menu s = new Menu();
                        foreach(var sm in subitems)
                            s.Add(((MenuItemWrapper)sm).m_item);
                        
                        m_item.Submenu = s;
                    }
                    
                    if (callback != null)
                    {
                        m_item.Activated += ClickHandler;
                        m_callback = callback;
                    }
                }
            }
            
            private void ClickHandler(object sender, EventArgs e)
            {
                m_callback();
            }

            /// <summary> Draw the image to the image menu item. Taken from: http://mono.1490590.n4.nabble.com/ImageMenuItem-does-not-display-the-image-Linux-platform-tp3510861p3511376.html </summary>
            ///  The event source. <see cref="System.Object"/> 
            ///  The event args. <see cref="Gtk.ExposeEventArgs"/> 
            private void DrawImageMenuItemImage(object o, Gtk.ExposeEventArgs args) 
            { 
                if (o as Gtk.ImageMenuItem == null) 
                        return; 
                        
                Gtk.Image image = (o as Gtk.ImageMenuItem).Image as Gtk.Image; 
                if (image == null || image.Pixbuf == null) 
                        return; 
        
                Gdk.GC mainGC = ((Gtk.Widget)o).Style.ForegroundGCs[(int)Gtk.StateType.Normal]; 
                Gdk.Rectangle r = args.Event.Area; 
                                
                args.Event.Window.DrawPixbuf(mainGC, image.Pixbuf, 0, 0, r.Left + 2, 
                                             r.Top + (r.Height - image.Pixbuf.Height) / 2, -1, -1, Gdk.RgbDither.None, 0, 0); 
            } 
            
            public string Text
            {
                get { return ((Gtk.Label)m_item.Child).Text; }
                set { ((Gtk.Label)m_item.Child).Text = value; }
            }
            
            public MenuIcons Icon
            {
                set { ((ImageMenuItem)m_item).Image = GetIcon(value); }
            }
            
            public bool Enabled
            {
                get { return m_item.Sensitive; }
                set { m_item.Sensitive = value; }
            }

            public bool Default
            {
                set { }
            }
        }
        
        protected StatusIcon m_trayIcon;
        protected Menu m_popupMenu;
        
        protected static Dictionary<TrayIcons, Pixbuf> _images = new Dictionary<TrayIcons, Pixbuf>();

        protected override void Exit ()
        {
            Application.Quit();
        }
  
        public override void Init (string[]  args)
        {
            CreateTrayInstance();
            base.Init (args); 
        }
        
        protected virtual void CreateTrayInstance()
        {
            m_trayIcon = new StatusIcon();
        }
        
        protected override void Run (string[] args)
        {
            m_trayIcon.Visible = true;
            m_trayIcon.PopupMenu += HandleTrayIconPopupMenu;
            Application.Run();
        }
        
        protected override IMenuItem CreateMenuItem (string text, MenuIcons icon, System.Action callback, IList<IMenuItem> subitems)
        {
            return new MenuItemWrapper(text, icon, callback, subitems);   
        }

        private void HandleTrayIconPopupMenu (object o, Gtk.PopupMenuArgs args)
        {
            m_popupMenu.ShowAll();
            m_popupMenu.Popup(null, null, null, 0u, 0u);
        }
        
        public static Gdk.Pixbuf ImageToPixbuf(System.Drawing.Image image)
        {
            using (var stream = new System.IO.MemoryStream()) 
            {
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                Gdk.Pixbuf pixbuf = new Gdk.Pixbuf(stream);
                return pixbuf;
            }
        }    

        public static Gtk.Image ImageToGtk(System.Drawing.Image image)
        {
            using (var stream = new System.IO.MemoryStream()) 
            {
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                Gtk.Image img = new Gtk.Image(stream);
                return img;
            }
        } 
        
        protected static Pixbuf GetIcon(TrayIcons icon)
        {
            if (!_images.ContainsKey(icon))
            {
                switch(icon)
                {
                case TrayIcons.Paused:
                    _images[icon] = ImageToPixbuf(Properties.Resources.TrayNormalPause.ToBitmap());
                    break;
                case TrayIcons.Running:
                    _images[icon] = ImageToPixbuf(Properties.Resources.TrayWorking.ToBitmap());
                    break;
                case TrayIcons.IdleError:
                    _images[icon] = ImageToPixbuf(Properties.Resources.TrayNormalError.ToBitmap());
                    break;
                case TrayIcons.RunningError:
                    _images[icon] = ImageToPixbuf(Properties.Resources.TrayWorking.ToBitmap());
                    break;
                case TrayIcons.PausedError:
                    _images[icon] = ImageToPixbuf(Properties.Resources.TrayNormalPause.ToBitmap());
                    break;
                case TrayIcons.Idle:
                default:
                    _images[icon] = ImageToPixbuf(Properties.Resources.TrayNormal.ToBitmap());
                    break;
                }
            }
            
            return _images[icon];
        }
        
        protected override TrayIcons Icon 
        {
            set 
            {
                m_trayIcon.Pixbuf = GetIcon(value);
            }
        }
        
        protected override void SetMenu (IEnumerable<IMenuItem> items)
        {
            m_popupMenu = new Menu();
            foreach(var itm in items)
                m_popupMenu.Add(((MenuItemWrapper)itm).MenuItem);
        }
        
        protected override void RegisterStatusUpdateCallback ()
        {
            Program.Connection.StatusUpdated += delegate(Duplicati.Server.Serialization.Interface.IServerStatus status) {
                Gtk.Application.Invoke(this, new StatusEventArgs(status), StatusUpdateEvent);
            };
        }
        
        protected void StatusUpdateEvent(object sender, EventArgs a)
        {
            if (a as StatusEventArgs == null)
                return;
            
            this.OnStatusUpdated(((StatusEventArgs)a).Status);
        }

        public override void Dispose()
        {
        }
        
    }
}

