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
using MonoMac.Foundation;
using MonoMac.AppKit;
using MonoMac.ObjCRuntime;
using System.Collections.Generic;

namespace Duplicati.GUI.TrayIcon
{
    [MonoMac.Foundation.Register("AppDelegate")]
    public class AppDelegate : NSApplicationDelegate
    {
        public override void AwakeFromNib ()
        {
            base.AwakeFromNib();
            
            CocoaRunner._instance.AwakeFromNib(this);
        }
    }
    
    public class CocoaRunner : TrayIconBase
    {
        public static CocoaRunner _instance;
        
        private AppDelegate m_appDelegate;
        
        public CocoaRunner()
            : base()
        {
            _instance = this;
        }
        
        private class MenuItemWrapper : IMenuItem
        {
            private NSMenuItem m_item;
            private Action m_callback;
            
            public NSMenuItem MenuItem { get { return m_item; } }
            
            public MenuItemWrapper(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
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
            public string Text {
                set {
                    m_item.Title = value;
                }
            }

            public MenuIcons Icon {
                set {
                }
            }

            public bool Enabled {
                set {
                    m_item.Enabled = value;
                }
            }

            public bool Default
            {
                set
                {
                }
            }
            #endregion
        }
        
        private static readonly string ICON_PATH = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "OSX Icons");
        
        private static readonly string ICON_NORMAL = System.IO.Path.Combine(ICON_PATH, "normal.png");
        private static readonly string ICON_PAUSED = System.IO.Path.Combine(ICON_PATH, "normal-pause.png");
        private static readonly string ICON_RUNNING = System.IO.Path.Combine(ICON_PATH, "normal-running.png");
        private static readonly string ICON_ERROR = System.IO.Path.Combine(ICON_PATH, "normal-error.png");
        
        private NSAutoreleasePool POOL;
        private NSStatusItem m_statusItem;
        private Dictionary<TrayIcons, NSImage> m_images = new Dictionary<TrayIcons, NSImage>();
        
        public override void Init (string[] args)
        {
            POOL = new NSAutoreleasePool();
            NSApplication.Init();
            NSApplication.Main(args);
        }
        
        public void AwakeFromNib(AppDelegate caller)
        {
            m_appDelegate = caller;
            m_statusItem = NSStatusBar.SystemStatusBar.CreateStatusItem(32);
            SetMenu(BuildMenu());
            RegisterStatusUpdateCallback();
            OnStatusUpdated(Program.Connection.Status);
            
            base.Init(null);
        }
        
        private NSImage GetIcon(TrayIcons icon)
        {
            if (!m_images.ContainsKey(icon))
            {
                switch(icon)
                {
                case TrayIcons.IdleError:
                    m_images[icon] = new NSImage(ICON_ERROR);
                    break;
                case TrayIcons.Paused:
                    m_images[icon] = new NSImage(ICON_PAUSED);
                    break;
                case TrayIcons.PausedError:
                    m_images[icon] = new NSImage(ICON_PAUSED);
                    break;
                case TrayIcons.Running:
                    m_images[icon] = new NSImage(ICON_RUNNING);
                    break;
                case TrayIcons.RunningError:
                    m_images[icon] = new NSImage(ICON_RUNNING);
                    break;
                case TrayIcons.Idle:
                default:
                    m_images[icon] = new NSImage(ICON_NORMAL);
                    break;
                }
            }
            
            return m_images[icon];
        }
        
        #region implemented abstract members of Duplicati.GUI.TrayIcon.TrayIconBase
        protected override void Run (string[] args)
        {
        }

        protected override TrayIcons Icon 
        {
            set 
            {
                m_statusItem.Image = GetIcon(value);
            }
        }

        protected override IMenuItem CreateMenuItem (string text, MenuIcons icon, Action callback, System.Collections.Generic.IList<IMenuItem> subitems)
        {
            return new MenuItemWrapper(text, icon, callback, subitems);
        }

        protected override void Exit ()
        {
            NSApplication.SharedApplication.Terminate(m_appDelegate);
        }

        protected override void SetMenu (System.Collections.Generic.IEnumerable<IMenuItem> items)
        {
            m_statusItem.Menu = new NSMenu();
            foreach(var itm in items)
                m_statusItem.Menu.AddItem(((MenuItemWrapper)itm).MenuItem);
        }

        public override void Dispose ()
        {
        }
        #endregion
    }
}

