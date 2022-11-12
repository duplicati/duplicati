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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.GUI.TrayIcon
{
    public class AvaloniaRunner : TrayIconBase
    {
        private AvaloniaApp application;
        private IEnumerable<AvaloniaMenuItem> menuItems = Enumerable.Empty<AvaloniaMenuItem>();

        public override void Init(string[] args)
        {
            //Init

            //Initialize and Run is called next
            base.Init(args);
        }

  

        protected override void UpdateUIState(Action action)
        {
            action.Invoke();
        }

        protected override void RegisterStatusUpdateCallback()
        {
            Program.Connection.OnStatusUpdated += delegate (IServerStatus status)
            {
                this.OnStatusUpdated(status);
            };
        }

        #region implemented abstract members of Duplicati.GUI.TrayIcon.TrayIconBase
        protected override void Run(string[] args)
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime()
            {
                Args = args,
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            var builder = Avalonia.Controls.AppBuilderBase<Avalonia.AppBuilder>.Configure<AvaloniaApp>()
                .UsePlatformDetect()
                .LogToTrace().SetupWithLifetime(lifetime);

            application = builder.Instance as AvaloniaApp;
            application.SetMenu(menuItems);

            lifetime.Start(args);
        }

        protected override IMenuItem CreateMenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            return new AvaloniaMenuItem(text, icon, callback, subitems);
        }

        protected override void NotifyUser(string title, string message, NotificationType type)
        {
            //var icon = Win32NativeNotifyIcon.InfoFlags.NIIF_INFO;

            switch (type)
            {
                case NotificationType.Information:
                    //icon = Win32NativeNotifyIcon.InfoFlags.NIIF_INFO;
                    break;
                case NotificationType.Warning:
                    //icon = Win32NativeNotifyIcon.InfoFlags.NIIF_WARNING;
                    break;
                case NotificationType.Error:
                    //icon = Win32NativeNotifyIcon.InfoFlags.NIIF_ERROR;
                    break;
            }

            //m_ntfIcon.ShowBalloonTip(title, message, icon);
        }

        protected override void Exit()
        {
            this.application?.Shutdown();
        }

        protected override void SetIcon(TrayIcons icon)
        {
            this.application?.SetIcon(icon);
        }

        protected override void SetMenu(IEnumerable<IMenuItem> items)
        {
            this.menuItems = items.Select(i => (AvaloniaMenuItem)i);
            this.application?.SetMenu(menuItems);
        }

        public override void Dispose()
        {
        }
        #endregion
    }

    public class AvaloniaMenuItem : IMenuItem
    {

        public string Text { get; private set; }
        public Action Callback { get; private set; }
        public IList<IMenuItem> SubItems { get; private set; }
        public bool Enabled { get; private set; }

        public AvaloniaMenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            if (subitems != null && subitems.Count > 0)
                throw new NotImplementedException("So far not needed.");

            this.Text = text;
            this.Callback = callback;
            this.SubItems = subitems;
            this.Enabled = true;
            SetIcon(icon);
        }

        #region IMenuItem implementation
        public void SetText(string text)
        {
            this.Text = text;
        }

        public void SetIcon(MenuIcons icon)
        {
            switch (icon)
            {
                case MenuIcons.Pause:
                    //this.Icon = Win32IconLoader.MenuPauseIcon;
                    break;
                case MenuIcons.Quit:
                    //this.Icon = Win32IconLoader.MenuQuitIcon;
                    break;
                case MenuIcons.Resume:
                    //this.Icon = Win32IconLoader.MenuPlayIcon;
                    break;
                case MenuIcons.Status:
                    //this.Icon = Win32IconLoader.MenuOpenIcon;
                    break;
                case MenuIcons.None:
                default:
                    //this.Icon = new SafeIconHandle(IntPtr.Zero);
                    break;
            }
        }

        public void SetEnabled(bool isEnabled)
        {
            this.Enabled = isEnabled;
        }

        public void SetDefault(bool value)
        {
            //TODO-DNC Cosmetic, not needed
        }
        #endregion

        public NativeMenuItem GetNativeItem()
        {
            var item = new NativeMenuItem(Text);
            item.IsEnabled = Enabled;
            item.Click += (_sender, _args) =>
            {
                Callback();
            };
            return item;
        }
    }

    public class AvaloniaApp : Application
    {
        private Avalonia.Controls.TrayIcon trayIcon;

        public override void Initialize()
        {
            //AvaloniaXamlLoader.Load(this);
        }

        public void SetIcon(TrayIcons icon)
        {
            //There are calls before the icon is created
            if (this.trayIcon == null)
                return;

            switch (icon)
            {
                case TrayIcons.IdleError:
                    this.trayIcon.Icon = LoadIcon("normal-error.png");
                    break;
                case TrayIcons.Paused:
                case TrayIcons.PausedError:
                     this.trayIcon.Icon = LoadIcon("normal-pause.png");
                    break;
                case TrayIcons.Running:
                case TrayIcons.RunningError:
                    this.trayIcon.Icon = LoadIcon("normal-running.png");
                    break;
                case TrayIcons.Idle:
                default:
                    this.trayIcon.Icon = LoadIcon("normal.png");
                    break;
            }
        }

        public void SetMenu(IEnumerable<AvaloniaMenuItem> menuItems)
        {
            // Reuse the menu on Mac
            var menu = trayIcon.Menu ?? new NativeMenu();
            menu.Items.Clear();
            foreach (var item in menuItems)
            {
                menu.Add(item.GetNativeItem());
            }
            trayIcon.Menu = menu;
        }

        public void Shutdown()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                throw new Exception("Unsupported Lifetime");
            }
        }

        private WindowIcon LoadIcon(string iconName)
        {
         var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();
        var bitmap = new Bitmap(assets.Open(new Uri($"avares://{Assembly.GetExecutingAssembly().FullName}/Assets/icons/" + iconName)));
        return  new WindowIcon(bitmap);
    }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var light = new FluentTheme(new Uri($"avares://{Assembly.GetExecutingAssembly().GetName()}")) { Mode = FluentThemeMode.Light };
                Styles.Add(light);

                var icon = LoadIcon("normal.png");
                var trayIcon = new Avalonia.Controls.TrayIcon();
                this.trayIcon = trayIcon;
                trayIcon.Icon = icon;
                trayIcon.ToolTipText = "Test";
                //The menu already exists on Mac but is nullable...
                var menu = trayIcon.Menu ?? new NativeMenu();
                var item = new NativeMenuItem("Quit");
                item.Click += (_sender, _args) =>
                {
                    desktop.Shutdown();
                };
                menu.Add(item);
                trayIcon.Menu = menu;

                // Register tray icons to be removed on application shutdown
                var icons = new Avalonia.Controls.TrayIcons();
                icons.Add(trayIcon);
                Avalonia.Controls.TrayIcon.SetIcons(Application.Current, icons);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
