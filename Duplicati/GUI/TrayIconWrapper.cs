#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace Duplicati.GUI
{
    /// <summary>
    /// This class wraps either a MWF NotifyIcon or a Gtk.StatusIcon
    /// and exposes properties as if it was a MWF NotifyIcon.
    /// 
    /// This fixes a bug with NotifyIcon not being implemented
    /// on Mac OSX, and the Gtk.StatusIcon not working if invoked
    /// from a thread other than main.
    /// 
    /// Use it instead of the standard MWF NotifyIcon.
    /// 
    /// The only difference is that you cannot just call Application.Run() as you
    /// would on windows/linux, but you must call:
    /// icon.Run();
    /// 
    /// This will invoke either Gtk.Run() or the Application.Run(), depending on
    /// the available framework. When exiting, call icon.Exit(), which will
    /// invoke the corresponding exit method
    /// 
    /// Since there is no way to detect when an image on a ToolStripMenuItem has
    /// been updated, the TextChanged event is used to also refresh the image,
    /// and your code must change the picture and then the text to trigger the update.
    /// 
    /// A small example:
    /// 
    /// void clickme(object sender, EventArgs args)
    /// {
    ///     if (sender is NotifyIcon)
    ///         Application.Exit();
    ///     else
    ///         ((TrayIconWrapper)sender).Exit();
    /// }
    /// 
    /// void Main()
    /// {
    ///     //If not using the wrapper, the code would look like:
    ///     NotifyIcon icon = new NotifyIcon();
    ///     icon.ContextMenuStrip = strip;
    ///     icon.Click += new EventHandler(clickme);
    ///     Application.Run();
    /// 
    ///     //With the wrapper, use this instead:
    ///     TrayIconWrapper icon = new TrayIconWrapper();
    ///     icon.ContextMenuStrip = strip;
    ///     icon.Click += new EventHandler(clickme);
    ///     icon.Run();
    ///     
    ///     //NOTE: You do not need both code fragments, the TrayIconWrapper 
    ///     will detect a windows platform and use a NotifyIcon automatically.
    /// }
    /// </summary>
    public class TrayIconWrapper
    {
        #region Private instance variables
        /// <summary>
        /// The MWF NotifyIcon instance
        /// </summary>
        private NotifyIcon m_notifyIcon;

        /// <summary>
        /// The Gtk.StatusIcon instance
        /// </summary>
        private /*Gtk.StatusIcon*/ object m_statusIcon;

        /// <summary>
        /// The list of wrapped Gtk menu items
        /// </summary>
        private MenuItemWrapper m_wrappedMenu;

        /// <summary>
        /// The owning form
        /// </summary>
        private Control m_owner;
        #endregion

        #region Variables that track the Gtk properties, as they are somewhat write-only
        /// <summary>
        /// The icon to display for the trayicon
        /// </summary>
        private Icon m_icon;
        /// <summary>
        /// A tooltip to display for the trayicon
        /// </summary>
        private string m_text;
        /// <summary>
        /// A value indicating if the element is visible
        /// </summary>
        private bool m_visible;
        #endregion

        #region Gtk reflection assemblies
        /// <summary>
        /// A reference to the Gtk assembly
        /// </summary>
        private System.Reflection.Assembly m_gtkasm;
        /// <summary>
        /// A reference to the Gdk assembly
        /// </summary>
        private System.Reflection.Assembly m_gdkasm;
        #endregion

        #region Callback helper class
        /// <summary>
        /// Internal class used to pass arguments when setting properties on the GTK object
        /// </summary>
        private class SetPropertyEventArgs : EventArgs
        {
            /// <summary>
            /// The object to which the property belongs
            /// </summary>
            private object m_owner;
            /// <summary>
            /// The name of the property
            /// </summary>
            private string m_propertyName;
            /// <summary>
            /// The value to set the property to
            /// </summary>
            private object m_value;

            /// <summary>
            /// Constructs a new property setting helper instance
            /// </summary>
            /// <param name="owner">The object to which the property belongs </param>
            /// <param name="propertyname">The name of the property</param>
            /// <param name="value">The value to set the property to</param>
            public SetPropertyEventArgs(object owner, string propertyname, object value)
            {
                m_owner = owner;
                m_propertyName = propertyname;
                m_value = value;
            }

            /// <summary>
            /// Helper method to set a property on the GTK item in the correct thread context
            /// </summary>
            /// <param name="sender">Dummy sender object</param>
            /// <param name="a">The set parameter object</param>
            public void SetProperty(object sender, EventArgs a)
            {
                System.Reflection.PropertyInfo pi = m_owner.GetType().GetProperty(m_propertyName);
                pi.SetValue(m_owner, m_value, null);
            }
        }
        #endregion

        #region Menu item wrapper
        /// <summary>
        /// Class that wraps a menuitem
        /// </summary>
        private class MenuItemWrapper
        {
            /// <summary>
            /// The context menu strip being wrapped, null on menus
            /// </summary>
            private ContextMenuStrip m_strip;

            /// <summary>
            /// The gtk popup menu, null on menus
            /// </summary>
            private /*Gtk.Menu*/ object m_popup;

            /// <summary>
            /// A reference to the wrapper, used to share assemblies, etc.
            /// </summary>
            private TrayIconWrapper m_icon;

            /// <summary>
            /// The ToolStripMenuItem that is being wrapped
            /// </summary>
            private ToolStripItem m_item;

            /// <summary>
            /// The matching Gtk.MenuItem, if required
            /// </summary>
            private /*Gtk.MenuItem*/ object m_menu;

            /// <summary>
            /// Wraps an entire ContextMenuStrip into a fake MenuItemWrapper root element
            /// </summary>
            /// <param name="icon">The trayicon that this menu belongs to, used to share reflection assemblies</param>
            /// <param name="useGtk">True if the wrapped menu should use Gtk elements</param>
            /// <param name="strip">The menu strip to wrap</param>
            /// <returns>A wrapped repesentation of the menu</returns>
            public MenuItemWrapper(TrayIconWrapper icon, ContextMenuStrip strip)
            {
                m_icon = icon;
                m_strip = strip;

                //m_popup = new Gtk.Menu();
                m_popup = m_icon.m_gtkasm.CreateInstance("Gtk.Menu");

                foreach (ToolStripItem x in strip.Items)
                {
                    MenuItemWrapper w = new MenuItemWrapper(m_icon, x);

                    //m_popup.Add(w.m_menu);
                    m_popup.GetType().GetMethod("Add", new Type[] { m_icon.m_gtkasm.GetType("Gtk.Widget") }).Invoke(m_popup, new object[] { w.m_menu });
                }
            }
            /// <summary>
            /// Constructs a new MenuItemWrapper, reflecting the input menuitem
            /// </summary>
            /// <param name="icon">The trayicon that this menu belongs to, used to share reflection assemblies</param>
            /// <param name="useGtk">True if the wrapped menu should use Gtk elements</param>
            /// <param name="strip">The menu strip to wrap</param>
            /// <returns>A wrapped repesentation of the menu</returns>
            private MenuItemWrapper(TrayIconWrapper icon, ToolStripItem item)
            {
                m_item = item;
                m_icon = icon;

                if (item is ToolStripSeparator)
                {
                    //m_menu = new Gtk.SeparatorMenuItem();
                    m_menu = m_icon.m_gtkasm.CreateInstance("Gtk.SeparatorMenuItem");
                }
                else
                {
                    Type mtype = m_icon.m_gtkasm.GetType("Gtk.ImageMenuItem");

                    //m_menu = new Gtk.ImageMenuItem(item.Text ?? "");
                    m_menu = Activator.CreateInstance(mtype, item.Text ?? "");
                    if (item.Image != null)
                        using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                        {
                            item.Image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Position = 0;

                            //((Gtk.ImageMenuItem)m_menu).Image = new Gtk.Image(ms);
                            Type imgtype = m_icon.m_gtkasm.GetType("Gtk.Image");
                            m_menu.GetType().GetProperty("Image").SetValue(m_menu, Activator.CreateInstance(imgtype, ms), null);
                        }

                    if (!string.IsNullOrEmpty(item.ToolTipText))
                    {
                        //m_menu.TooltipText = item.ToolTipText;
                        mtype.GetProperty("TooltipText").SetValue(m_menu, item.ToolTipText, null);
                    }

                    //m_menu.Sensitive = item.Enabled;
                    mtype.GetProperty("Sensitive").SetValue(m_menu, item.Enabled, null);

                    //m_menu.Activated += new EventHandler(menu_Activated);
                    mtype.GetEvent("Activated").AddEventHandler(m_menu, new EventHandler(menu_Activated));

                    item.EnabledChanged += new EventHandler(item_EnabledChanged);
                    item.TextChanged += new EventHandler(item_TextChanged);

                    if (item is ToolStripMenuItem)
                        if ((((ToolStripMenuItem)item).DropDownItems).Count > 0)
                        {
                            //m_menu.Submenu = new Gtk.Menu();
                            object submenu = m_icon.m_gtkasm.CreateInstance("Gtk.Menu");
                            mtype.GetProperty("Submenu").SetValue(m_menu, submenu, null);

                            foreach (ToolStripItem x in ((ToolStripMenuItem)item).DropDownItems)
                            {
                                MenuItemWrapper w = new MenuItemWrapper(m_icon, x);
                                
                                //((Gtk.Menu)m_menu.Submenu).Add(w.m_menu);
                                submenu.GetType().GetMethod("Add", new Type[] { m_icon.m_gtkasm.GetType("Gtk.Widget") }).Invoke(submenu, new object[] { w.m_menu });
                            }
                        }
                }


            }

            /// <summary>
            /// If the text changes, update the wrapped Gtk menu
            /// </summary>
            /// <param name="sender">Unused sender argument</param>
            /// <param name="e">Unused event argument</param>
            void item_TextChanged(object sender, EventArgs e)
            {
                //((Gtk.AccelLabel)m_menu.Child).Text = m_item.Text;
                object child = m_menu.GetType().GetProperty("Child").GetValue(m_menu, null);
                child.GetType().GetProperty("Text").SetValue(child, m_item.Text, null);

                if (m_item.Image == null)
                {
                    //((Gtk.ImageMenuItem)m_menu).Image = null;
                    m_menu.GetType().GetProperty("Image").SetValue(m_menu, null, null);
                }
                else
                {
                    using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                    {
                        m_item.Image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;

                        //((Gtk.ImageMenuItem)m_menu).Image = new Gtk.Image(ms);
                        Type imgtype = m_icon.m_gtkasm.GetType("Gtk.Image");
                        m_menu.GetType().GetProperty("Image").SetValue(m_menu, Activator.CreateInstance(imgtype, ms), null);
                    }
                }
            }

            /// <summary>
            /// If the item is clicked, click the real menu
            /// </summary>
            /// <param name="sender">Unused sender argument</param>
            /// <param name="e">Unused event argument</param>
            private void menu_Activated(object sender, EventArgs e)
            {
                m_item.PerformClick();
            }

            /// <summary>
            /// If the item is enabled or disabled, reflect this on the wrapped Gtk menu
            /// </summary>
            /// <param name="sender">Unused sender argument</param>
            /// <param name="e">Unused event argument</param>
            private void item_EnabledChanged(object sender, EventArgs e)
            {
                //m_menu.Sensitive = m_item.Enabled;
                m_menu.GetType().GetProperty("Sensitive").SetValue(m_menu, m_item.Enabled, null);
            }

            /// <summary>
            /// Displays the popup menu
            /// </summary>
            public void Popup()
            {
                //m_popup.ShowAll();
                m_popup.GetType().GetMethod("ShowAll").Invoke(m_popup, null);

                //Special Gtk "feature", must send 0 as button:
                // http://www.spinics.net/lists/gtk/msg04106.html
                //m_popup.Popup(null, null, null, 0, 0);
                System.Reflection.MethodInfo mi = m_popup.GetType().GetMethod("Popup", new Type[]{ m_icon.m_gtkasm.GetType("Gtk.Widget"), m_icon.m_gtkasm.GetType("Gtk.Widget"), m_icon.m_gtkasm.GetType("Gtk.MenuPositionFunc"), typeof(uint), typeof(uint) });
                mi.Invoke(m_popup, new object[] { null, null, null, 0u, 0u });

                //((Gtk.Menu)m_popup).Popup(
            }

            /// <summary>
            /// Gets the context menu strip that is being wrapped
            /// </summary>
            public ContextMenuStrip Strip { get { return m_strip; } }
        }

        #endregion

        #region Public interface

        /// <summary>
        /// An event that is raised when the user clicks the TrayIcon, this is never activated on Mac OSX
        /// </summary>
        public event MouseEventHandler MouseClick;
        /// <summary>
        /// An event that is raised when the user doubleclicks the TrayIcon, this is never activated on Mac OSX
        /// </summary>
        public event MouseEventHandler MouseDoubleClick;

        /// <summary>
        /// Constructs a new trayicon wrapper
        /// </summary>
        /// <param name="owner">The form that owns the icon, used to perform threadsafe invocations</param>
        public TrayIconWrapper(Control owner)
        {
            m_owner = owner;

            if (Duplicati.Library.Core.Utility.IsClientLinux)
            {
                try
                {
                    //These give deprecation warnings, because it can be tricky to do this,
                    // but in this special case, we actually want the newest gtk we can find.
                    m_gtkasm = System.Reflection.Assembly.LoadWithPartialName("gtk-sharp");
                    m_gdkasm = System.Reflection.Assembly.LoadWithPartialName("gdk-sharp");

                    if (m_gtkasm != null && m_gdkasm != null)
                    {
                        //Make sure we have Gtk 2.0 or better
                        if (m_gtkasm.GetName().Version < new Version(2, 0))
                            throw new Exception("Too old gtk: " + m_gtkasm.GetName().Version.ToString());
                        if (m_gdkasm.GetName().Version < new Version(2, 0))
                            throw new Exception("Too old gdk: " + m_gtkasm.GetName().Version.ToString());

                        PrepareGtkIcon();
                    }
                    else
                        throw new Exception("Unable to locate gtk assemblies");
                }
                catch(Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                    //We failed to load the gtk libraries, this should be logged
                }
            }

            //If we could not get a Gtk icon, just use MWF
            if (m_statusIcon == null)
            {
                m_notifyIcon = new NotifyIcon();
                m_notifyIcon.MouseClick += new MouseEventHandler(notifyIcon_MouseClick);
                m_notifyIcon.MouseDoubleClick += new MouseEventHandler(notifyIcon_MouseDoubleClick);
            }
        }

        /// <summary>
        /// Starts the message pumps of Gtk or MWF as required
        /// </summary>
        public void Run()
        {
            if (m_statusIcon == null)
                System.Windows.Forms.Application.Run(m_owner as Form);
            else
            {
                if (m_owner as Form != null)
                    (m_owner as Form).Show();

                Type app = m_gtkasm.GetType("Gtk.Application");
                app.GetMethod("Run", new Type[0]).Invoke(null, null);
            }
        }

        /// <summary>
        /// Stops the message pump of either Gtk or MWF as required
        /// </summary>
        public void Exit()
        {
            if (m_statusIcon == null)
                System.Windows.Forms.Application.Exit();
            else
            {
                Type app = m_gtkasm.GetType("Gtk.Application");
                app.GetMethod("Quit", new Type[0]).Invoke(null, null);
            }
        }

        /// <summary>
        /// Gets or sets the icon displayed by the TrayIcon
        /// </summary>
        public Icon Icon
        {
            get
            {
                if (m_notifyIcon != null)
                    return m_notifyIcon.Icon;
                else
                    return m_icon;
            }
            set
            {
                if (m_notifyIcon != null)
                    m_notifyIcon.Icon = value;
                else
                {
                    m_icon = value;

                    using (Bitmap bmp = m_icon.ToBitmap())
                    using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;

                        //InvokeGtk("Pixbuf", new Gdk.Pixbuf(ms));
                        InvokeGtk("Pixbuf", Activator.CreateInstance(m_gdkasm.GetType("Gdk.Pixbuf"), ms));
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the tooltip displayed by the TrayIcon
        /// </summary>
        public string Text
        {
            get
            {
                if (m_notifyIcon != null)
                    return m_notifyIcon.Text;
                else
                    return m_text;
            }
            set
            {
                if (m_notifyIcon != null)
                    m_notifyIcon.Text = value;
                else
                {
                    m_text = value;

                    InvokeGtk("Tooltip", m_text);
                }
            }
        }

        /// <summary>
        /// Gets or sets the visibility of the TrayIcon
        /// </summary>
        public bool Visible
        {
            get
            {
                if (m_notifyIcon != null)
                    return m_notifyIcon.Visible;
                else
                    return m_visible;
            }
            set
            {
                if (m_notifyIcon != null)
                    m_notifyIcon.Visible = value;
                else
                {
                    m_visible = value;
                    InvokeGtk("Visible", m_visible);
                }
            }
        }

        /// <summary>
        /// Gets or sets the context menu for the TrayIcon
        /// </summary>
        public ContextMenuStrip ContextMenuStrip
        {
            get
            {
                if (m_notifyIcon != null)
                    return m_notifyIcon.ContextMenuStrip;
                else
                    return m_wrappedMenu == null ? null : m_wrappedMenu.Strip;
            }
            set
            {
                if (value == null)
                {
                    if (m_notifyIcon != null)
                        m_notifyIcon.ContextMenuStrip = null;
                    else
                        m_wrappedMenu = null;
                }
                else
                {
                    if (m_notifyIcon != null)
                        m_notifyIcon.ContextMenuStrip = value;
                    else
                        m_wrappedMenu = new MenuItemWrapper(this, value);
                }
            }
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Helper method that instanciates and prepares the Gtk StausIcon
        /// </summary>
        private void PrepareGtkIcon()
        {
            //Gtk.Application.Init();
            Type app = m_gtkasm.GetType("Gtk.Application");
            app.GetMethod("Init", new Type[0]).Invoke(null, null);

            //m_statusIcon = new Gtk.StatusIcon();
            Type icon = m_gtkasm.GetType("Gtk.StatusIcon");
            m_statusIcon = Activator.CreateInstance(icon);

            //m_statusIcon.Visible = false;
            icon.GetProperty("Visible").SetValue(m_statusIcon, false, null);

            //m_statusIcon.Activate += new EventHandler(m_statusIcon_Activate);
            icon.GetEvent("Activate").AddEventHandler(m_statusIcon, new EventHandler(statusIcon_Activate));

            //m_statusIcon.PopupMenu += new Gtk.PopupMenuHandler(m_statusIcon_PopupMenu);
            Type popupHandler = icon.GetEvent("PopupMenu").EventHandlerType;
            System.Reflection.MethodInfo mi = this.GetType().GetMethod("statusIcon_PopupMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            icon.GetEvent("PopupMenu").AddEventHandler(m_statusIcon, Delegate.CreateDelegate(popupHandler, this, mi));

            m_wrappedMenu = null;
        }

        /// <summary>
        /// Sets a property on the StatusIcon, using the correct thread context
        /// </summary>
        /// <param name="propname">The name of the property to set</param>
        /// <param name="value">The value to set the property to</param>
        private void InvokeGtk(string propname, object value)
        {
            SetPropertyEventArgs args = new SetPropertyEventArgs(m_statusIcon, propname, value);
            //Gtk.Application.Invoke(this, args, new EventHandler(args.Execute));
            m_gtkasm.GetType("Gtk.Application")
                .GetMethod("Invoke", new Type[] { typeof(object), typeof(EventArgs), typeof(EventHandler) })
                .Invoke(null, new object[] { this, args, new EventHandler(args.SetProperty) });
        }

        /// <summary>
        /// Invokes the Gtk.Application.Quit() method to cleanly shut down Gtk
        /// </summary>
        /// <param name="o">Unused sender object</param>
        /// <param name="a">Unused EventArgs object</param>
        private void InvokeQuit(object o, EventArgs a)
        {
            //Gtk.Application.Quit();
            m_gtkasm.GetType("Gtk.Application").GetMethod("Quit", new Type[0]).Invoke(null, null);
        }

        /// <summary>
        /// Eventhandler for the PopupMenu event
        /// </summary>
        /// <param name="o">Unused sender object</param>
        /// <param name="a">Unused EventArgs object</param>
        private void statusIcon_PopupMenu(object o, object args)
        {
            if (m_owner.InvokeRequired)
                m_owner.Invoke(new EventHandler(statusIcon_PopupMenu), o, args);
            else
            {
                if (m_wrappedMenu != null)
                    m_wrappedMenu.Popup();
            }
        }

        /// <summary>
        /// Eventhandler for the Activate event, never invoked on Mac OSX
        /// </summary>
        /// <param name="o">Unused sender object</param>
        /// <param name="a">Unused EventArgs object</param>
        private void statusIcon_Activate(object sender, EventArgs e)
        {
            if (m_owner.InvokeRequired)
                m_owner.Invoke(new EventHandler(statusIcon_Activate), sender, e);
            else
            {
                if (MouseClick != null)
                    MouseClick(this, new MouseEventArgs(MouseButtons.Left, 1, Cursor.Position.X, Cursor.Position.Y, 0));
            }
        }

        /// <summary>
        /// Eventhandler for the doubleclick event, never invoked on Mac OSX
        /// </summary>
        /// <param name="o">Unused sender object</param>
        /// <param name="a">The mouseevents</param>
        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (MouseDoubleClick != null)
                MouseDoubleClick(this, e);
        }

        /// <summary>
        /// Eventhandler for the click event, never invoked on Mac OSX
        /// </summary>
        /// <param name="o">Unused sender object</param>
        /// <param name="a">The mouseevents</param>
        private void notifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (MouseClick != null)
                MouseClick(this, e);
        }

        #endregion
    }
}
