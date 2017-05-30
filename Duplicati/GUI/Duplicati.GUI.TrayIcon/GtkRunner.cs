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
#if __MonoCS__ || __WindowsGTK__ || ENABLE_GTK
using System;
using System.Collections.Generic;
using Gdk;
using Gtk;

namespace Duplicati.GUI.TrayIcon
{
    public class GtkRunner : TrayIconBase
    {
        private static string m_svgfolder;

        /// <summary>
        /// Static constructor that ensures the Gtk environment is initialized
        /// </summary>
        static GtkRunner()
        {
            m_svgfolder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SVGIcons");
            m_svgfolder = System.IO.Path.Combine(m_svgfolder, "dark");

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

            private string GetFilenameForIcon(MenuIcons icon)
            {
                switch (icon)
                {
                    case MenuIcons.Pause:
                        return "context_menu_pause";
                    case MenuIcons.Quit:
                        return "context_menu_quit";
                    case MenuIcons.Resume:
                        return "context_menu_resume";
                    case MenuIcons.Status:
                        return "context_menu_open";
                    default:
                        return null;
                }
            }
            
            private Gtk.Image GetIcon(MenuIcons icon)
            {
                if (!_icons.ContainsKey(icon))
                {
                    _icons[icon] = null;
                        
                    var filename = GetFilenameForIcon(icon);
                    if (filename != null)
                    {
                        filename = System.IO.Path.Combine(m_svgfolder, System.IO.Path.ChangeExtension(filename, ".svg"));
                        if (System.IO.File.Exists(filename))
                            _icons[icon] = new Gtk.Image(filename);
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
                    if (!Duplicati.Library.Utility.Utility.IsClientOSX)
                        if (icon != MenuIcons.None) {
                            ((ImageMenuItem)m_item).Image = GetIcon(icon);

                            // On some (older versions) of GDK, this hack is required
                            //m_item.ExposeEvent += DrawImageMenuItemImage;
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

            /*
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
            */
            
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
            // Sometimes the tray icon will not display
            // if it is not created with an icon
            m_trayIcon = StatusIcon.NewFromStock(Stock.About);
        }
        
        protected override void Run (string[] args)
        {
            m_trayIcon.Visible = true;
            m_trayIcon.PopupMenu += HandleTrayIconPopupMenu;
            m_trayIcon.Activate += HandleTrayIconActivate;
            Application.Run();
        }
        
        protected override IMenuItem CreateMenuItem (string text, MenuIcons icon, System.Action callback, IList<IMenuItem> subitems)
        {
            return new MenuItemWrapper(text, icon, callback, subitems);   
        }

        private void HandleTrayIconPopupMenu (object o, Gtk.PopupMenuArgs args)
        {
            // TODO: Does not work on Fedora
            m_popupMenu.ShowAll();
            m_popupMenu.Popup(null, null, null, 0u, 0u);
        }

        private void HandleTrayIconActivate (object o, EventArgs args)
        {
            base.ShowStatusWindow();
        }
        
        protected override void UpdateUIState(System.Action action)
        {
            Gtk.Application.Invoke((sender, arg) => {
                action();
            });
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

        protected static string GetTrayIconFilename(TrayIcons icon)
        {
            switch (icon)
            {
                case TrayIcons.Paused:
                    return "normal-pause";
                case TrayIcons.Running:
                    return "normal-running";
                case TrayIcons.IdleError:
                    return "normal-error";
                case TrayIcons.RunningError:
                    return "normal-running";
                case TrayIcons.PausedError:
                    return "normal-pause";
                case TrayIcons.Idle:
                default:
                    return "normal";
            }            
        }
        
        protected static Pixbuf GetIcon(TrayIcons icon)
        {
            if (!_images.ContainsKey(icon))
            {
                if (Duplicati.Library.Utility.Utility.IsClientOSX)
                {
                    switch (icon)
                    {
                        case TrayIcons.Paused:
                            _images[icon] = ImageToPixbuf(ImageLoader.LoadIcon(ImageLoader.PauseIcon).ToBitmap());
                            break;
                        case TrayIcons.Running:
                            _images[icon] = ImageToPixbuf(ImageLoader.LoadIcon(ImageLoader.WorkingIcon).ToBitmap());
                            break;
                        case TrayIcons.IdleError:
                            _images[icon] = ImageToPixbuf(ImageLoader.LoadIcon(ImageLoader.ErrorIcon).ToBitmap());
                            break;
                        case TrayIcons.RunningError:
                            _images[icon] = ImageToPixbuf(ImageLoader.LoadIcon(ImageLoader.WorkingIcon).ToBitmap());
                            break;
                        case TrayIcons.PausedError:
                            _images[icon] = ImageToPixbuf(ImageLoader.LoadIcon(ImageLoader.PauseIcon).ToBitmap());
                            break;
                        case TrayIcons.Idle:
                        default:
                            _images[icon] = ImageToPixbuf(ImageLoader.LoadIcon(ImageLoader.NormalIcon).ToBitmap());
                            break;
                    }
                }
                else
                {
                    _images[icon] = null;
                    var filename = System.IO.Path.Combine(m_svgfolder, System.IO.Path.ChangeExtension(GetTrayIconFilename(icon), ".svg"));
                    if (System.IO.File.Exists(filename))
                        _images[icon] = new Pixbuf(filename);
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
            Program.Connection.OnStatusUpdated += delegate(Duplicati.Server.Serialization.Interface.IServerStatus status) {
                Gtk.Application.Invoke(this, new StatusEventArgs(status), StatusUpdateEvent);
            };
        }

        protected override void NotifyUser(string title, string message, NotificationType type)
        {
            try
            {
                // We guard it like this to allow running on systems without sharp-notify
                RealNotifyUser(title, message, type);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to send notification: {0}", ex);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void RealNotifyUser(string title, string message, NotificationType type)
        {
            var icon = Stock.Info;
            switch (type)
            {
                case NotificationType.Information:
                    icon = Stock.DialogInfo;
                    break;
                case NotificationType.Warning:
                    icon = Stock.DialogWarning;
                    break;
                case NotificationType.Error:
                    icon = Stock.DialogError;
                    break;
            }

            var notification = new Notifications.Notification(title, message, Stock.Info);
            notification.Show();
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
#endif
