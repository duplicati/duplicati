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
using MonoMac.Foundation;
using MonoMac.AppKit;
using MonoMac.ObjCRuntime;
using System.Collections.Generic;
using MonoMac.CoreGraphics;
using System.IO;

namespace Duplicati.GUI.TrayIcon
{
    public class CocoaRunner : Duplicati.GUI.TrayIcon.TrayIconBase
    {        
        private class MenuItemWrapper : Duplicati.GUI.TrayIcon.IMenuItem
        {
            private readonly NSMenuItem m_item;
            private readonly Action m_callback;
            
            public NSMenuItem MenuItem { get { return m_item; } }
            
            public MenuItemWrapper(string text, Action callback, IList<Duplicati.GUI.TrayIcon.IMenuItem> subitems)
            {
                if (text == "-")
                    m_item = NSMenuItem.SeparatorItem;
                else
                {
                    m_item = new NSMenuItem(text, ClickHandler);
                    m_callback = callback;
                    
                    if (subitems != null && subitems.Count > 0)
                    {
                        m_item.Submenu = new NSMenu();
                        foreach(var itm in subitems)
                            m_item.Submenu.AddItem(((MenuItemWrapper)itm).MenuItem);
                    }
                }
            }
            
            private void ClickHandler(object sender, EventArgs args)
            {
                if (m_callback != null)
                    m_callback();
            }
            
            #region IMenuItem implementation
            public void SetText(string text)
            {
                m_item.Title = text;
            }

            public void SetIcon(MenuIcons icons)
            {
                // Do nothing.  Implementation needed for IMenuItem interface.
            }

            public void SetDefault(bool isDefault)
            {
                // Do nothing.  Implementation needed for IMenuItem interface.
            }
            #endregion
        }
        
        private static readonly System.Reflection.Assembly ASSEMBLY = System.Reflection.Assembly.GetExecutingAssembly();
        private static readonly string ICON_PATH = ASSEMBLY.GetName().Name + ".TrayResources.OSXIcons.";
        
        private static readonly string ICON_NORMAL = ICON_PATH + "normal.png";
        private static readonly string ICON_PAUSED = ICON_PATH + "normal-pause.png";
        private static readonly string ICON_RUNNING = ICON_PATH + "normal-running.png";
        private static readonly string ICON_WARNING = ICON_PATH + "normal-error.png"; // TODO: create a normal-warning.png, for now use normal-error.png
        private static readonly string ICON_ERROR = ICON_PATH + "normal-error.png";
        
        private NSStatusItem m_statusItem;
        private readonly Dictionary<Duplicati.GUI.TrayIcon.TrayIcons, NSImage> m_images = new Dictionary<Duplicati.GUI.TrayIcon.TrayIcons, NSImage>();
        private NSApplication m_app;

        // We need to keep the items around, otherwise the GC will destroy them and crash the app
        private readonly List<Duplicati.GUI.TrayIcon.IMenuItem> m_keeper = new List<Duplicati.GUI.TrayIcon.IMenuItem>();

        public override void Init(string[] args)
        {
            NSApplication.Init();
            m_app = NSApplication.SharedApplication;
            m_app.ActivateIgnoringOtherApps(true);

            m_statusItem = NSStatusBar.SystemStatusBar.CreateStatusItem(32);
            m_statusItem.HighlightMode = true;

            base.Init(args);
        }

        private NSImage LoadStream(System.IO.Stream s)
        {
            using(var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                ms.Flush();
                ms.Close();

                var b = ms.ToArray();

                var dp = new CGDataProvider(b, 0, b.Length);
                var img2 = CGImage.FromPNG(dp, null, false, CGColorRenderingIntent.Default);

                return new NSImage(img2, new System.Drawing.SizeF(18, 18));
            }
        }

        private NSImage GetIcon(Duplicati.GUI.TrayIcon.TrayIcons icon)
        {
            if (!m_images.ContainsKey(icon))
            {
                switch(icon)
                {
                    case Duplicati.GUI.TrayIcon.TrayIcons.IdleError:
                        m_images[icon] = LoadStream(ASSEMBLY.GetManifestResourceStream(ICON_ERROR));
                        break;
                    case Duplicati.GUI.TrayIcon.TrayIcons.IdleWarning:
                        m_images[icon] = LoadStream(ASSEMBLY.GetManifestResourceStream(ICON_WARNING));
                        break;
                    case Duplicati.GUI.TrayIcon.TrayIcons.Paused:
                        m_images[icon] = LoadStream(ASSEMBLY.GetManifestResourceStream(ICON_PAUSED));
                        break;
                    case Duplicati.GUI.TrayIcon.TrayIcons.PausedError:
                        m_images[icon] = LoadStream(ASSEMBLY.GetManifestResourceStream(ICON_PAUSED));
                        break;
                    case Duplicati.GUI.TrayIcon.TrayIcons.Running:
                        m_images[icon] = LoadStream(ASSEMBLY.GetManifestResourceStream(ICON_RUNNING));
                        break;
                    case Duplicati.GUI.TrayIcon.TrayIcons.RunningError:
                        m_images[icon] = LoadStream(ASSEMBLY.GetManifestResourceStream(ICON_RUNNING));
                        break;
                    case Duplicati.GUI.TrayIcon.TrayIcons.Idle:
                    default:
                        m_images[icon] = LoadStream(ASSEMBLY.GetManifestResourceStream(ICON_NORMAL));
                        break;
                }
            }
            
            return m_images[icon];
        }
        
        #region implemented abstract members of Duplicati.GUI.TrayIcon.TrayIconBase
        protected override void Run(string[] args)
        {
            try
            {
                m_app.Run();
            }
            finally
            {
                if (m_statusItem != null)
                {
                    NSStatusBar.SystemStatusBar.RemoveStatusItem(m_statusItem);
                    m_statusItem = null;
                    m_keeper.Clear();
                    m_images.Clear();
                }
                m_app = null;
            }
        }
        
        protected override void UpdateUIState(Action action)
        {
            if (m_app != null)
                m_app.BeginInvokeOnMainThread(() => { 
                    action();
                });
            else
                action();
        }

        protected override Duplicati.GUI.TrayIcon.IMenuItem CreateMenuItem (string text, Duplicati.GUI.TrayIcon.MenuIcons icon, Action callback, System.Collections.Generic.IList<Duplicati.GUI.TrayIcon.IMenuItem> subitems)
        {
            return new MenuItemWrapper(text, callback, subitems);
        }

        protected override void Exit()
        {
            if (m_app != null)
            {
                // Set the flag
                m_app.Stop(m_app);
                // Post an event to trigger the exit
                m_app.PostEvent(NSEvent.OtherEvent(NSEventType.ApplicationDefined, 
                    new System.Drawing.PointF(0,0),
                    0, 0, 0, null, 0, 0, 0), true);
            }
        }

        protected override void SetIcon(TrayIcons icon)
        {
            m_statusItem.Image = GetIcon(icon);
        }

        protected override void SetMenu(System.Collections.Generic.IEnumerable<Duplicati.GUI.TrayIcon.IMenuItem> items)
        {
            m_statusItem.Menu = new NSMenu();
            m_keeper.AddRange(items);
            foreach(var itm in items)
                m_statusItem.Menu.AddItem(((MenuItemWrapper)itm).MenuItem);
        }

        protected override void NotifyUser(string title, string message, NotificationType type)
        {
            var notification = new NSUserNotification();
            notification.Title = title;
            notification.InformativeText = message;
            notification.DeliveryDate = DateTime.Now;
            notification.SoundName = NSUserNotification.NSUserNotificationDefaultSoundName;
 
            // We get the Default notification Center
            var center = NSUserNotificationCenter.DefaultUserNotificationCenter;

            // TODO: Figure out why this does not work
            if (center == null)
                return;

            //center.DidDeliverNotification += (s, e) => { };

            center.DidActivateNotification += (s, e) => { };

            // If we return true here, Notification will show up even if your app is TopMost.
            center.ShouldPresentNotification = (c, n) =>
            {
                return true;
            };

            center.ScheduleNotification(notification);
        }

        public override void Dispose ()
        {
        }
        #endregion
    }
}
