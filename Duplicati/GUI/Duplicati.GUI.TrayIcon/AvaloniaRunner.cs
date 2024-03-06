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
            base.Init(args);
        }

        protected override void UpdateUIState(Action action)
            => RunOnUIThread(action);

        protected override void RegisterStatusUpdateCallback()
        {
            Program.Connection.OnStatusUpdated += delegate (IServerStatus status)
            {
                this.OnStatusUpdated(status);
            };
        }

        #region implemented abstract members of Duplicati.GUI.TrayIcon.TrayIconBase
        protected void Run(string[] args)
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime()
            {
                Args = args,
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            var builder = AppBuilderBase<Avalonia.AppBuilder>
                .Configure<AvaloniaApp>()
                .UsePlatformDetect()
                .LogToTrace()
                .With(new MacOSPlatformOptions() { ShowInDock = false })
                .SetupWithLifetime(lifetime);

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
            UpdateUIState(() => this.application?.Shutdown() );
        }

        protected override void SetIcon(TrayIcons icon)
        {
            UpdateUIState(() => this.application?.SetIcon(icon));
        }

        protected override void SetMenu(IEnumerable<IMenuItem> items)
        {
            this.menuItems = items.Select(i => (AvaloniaMenuItem)i);
            if (this.application != null)
                UpdateUIState(() => this.application?.SetMenu(menuItems));
        }

        public override void Dispose() 
        {
            GC.SuppressFinalize(this);
        }
        #endregion

        internal static void RunOnUIThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Post(action);
        }

        internal static IBitmap LoadBitmap(string iconName)
        {
            var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();
            return new Bitmap(assets.Open(new Uri($"avares://{Assembly.GetExecutingAssembly().FullName}/Assets/icons/" + iconName)));
        }

        internal static WindowIcon LoadIcon(string iconName)
        {
            return new WindowIcon(LoadBitmap(iconName));
        }
    }

    public class AvaloniaMenuItem : IMenuItem
    {

        public string Text { get; private set; }
        public Action Callback { get; private set; }
        public IList<IMenuItem> SubItems { get; private set; }
        public bool Enabled { get; private set; }
        public MenuIcons Icon { get; private set; }
        public bool IsDefault { get; private set; }
        private NativeMenuItem? nativeMenuItem;

        public AvaloniaMenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            if (subitems != null && subitems.Count > 0)
                throw new NotImplementedException("So far not needed.");

            this.Text = text;
            this.Callback = callback;
            this.SubItems = subitems;
            this.Enabled = true;
            this.Icon = icon;
        }

        #region IMenuItem implementation
        public void SetText(string text)
        {
            this.Text = text;
            if (nativeMenuItem != null)
                AvaloniaRunner.RunOnUIThread(() => nativeMenuItem.Header = text);

        }

        public void SetIcon(MenuIcons icon)
        {
            this.Icon = icon;
            if (nativeMenuItem != null)
                AvaloniaRunner.RunOnUIThread(() => this.UpdateIcon());
        }

        /// <summary>
        /// Performs unguarded update of the icon, ensure calling thread is UI 
        /// and the nativeMenuItem has been set
        /// </summary>
        private void UpdateIcon()
        {
            nativeMenuItem.Icon = this.Icon switch {
                MenuIcons.Status => AvaloniaRunner.LoadBitmap("context-menu-open.png"),
                MenuIcons.Quit => AvaloniaRunner.LoadBitmap("context-menu-quit.png"),
                MenuIcons.Pause => AvaloniaRunner.LoadBitmap("context-menu-pause.png"),
                MenuIcons.Resume => AvaloniaRunner.LoadBitmap("context-menu-resume.png"),
                _ => null
            };

        }

        public void SetEnabled(bool isEnabled)
        {
            this.Enabled = isEnabled;
            if (nativeMenuItem != null)
                AvaloniaRunner.RunOnUIThread(() => nativeMenuItem.IsEnabled = this.Enabled);
        }

        public void SetDefault(bool value)
        {
            this.IsDefault = value;
            // Not currently supported by Avalonia, 
            // used to set the menu bold on Windows to indicate the default entry
        }
        #endregion

        public NativeMenuItem GetNativeItem()
        {
            if (this.nativeMenuItem == null)
            {
                this.nativeMenuItem = new NativeMenuItem(Text)
                {
                    IsEnabled = Enabled
                };

                this.UpdateIcon();
                this.nativeMenuItem.Click += (_, _) =>
                {
                    Callback();
                };
            }
            return this.nativeMenuItem;
        }
    }

    public class AvaloniaApp : Application
    {
        private Avalonia.Controls.TrayIcon trayIcon;

        private List<AvaloniaMenuItem> menuItems;

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
                    this.trayIcon.Icon = AvaloniaRunner.LoadIcon("normal-error.png");
                    break;
                case TrayIcons.Paused:
                case TrayIcons.PausedError:
                    this.trayIcon.Icon = AvaloniaRunner.LoadIcon("normal-pause.png");
                    break;
                case TrayIcons.Running:
                case TrayIcons.RunningError:
                    this.trayIcon.Icon = AvaloniaRunner.LoadIcon("normal-running.png");
                    break;
                case TrayIcons.Idle:
                default:
                    this.trayIcon.Icon = AvaloniaRunner.LoadIcon("normal.png");
                    break;
            }
        }

        public void SetMenu(IEnumerable<AvaloniaMenuItem> menuItems)
        {
            this.menuItems = menuItems.ToList();
            if (trayIcon != null)
            {
                // Reuse the menu on Mac
                var menu = trayIcon.Menu ?? new NativeMenu();
                menu.Items.Clear();
                foreach (var item in this.menuItems)
                {
                    menu.Add(item.GetNativeItem());
                }
                trayIcon.Menu = menu;
            }
        }

        public void Shutdown()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();      
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var light = new FluentTheme(new Uri($"avares://{Assembly.GetExecutingAssembly().GetName()}")) { Mode = FluentThemeMode.Light };
            Styles.Add(light);

            var icon = AvaloniaRunner.LoadIcon("normal.png");
            this.trayIcon = new Avalonia.Controls.TrayIcon() { Icon = icon};
            
            // Handle being loaded with menu items
            if (menuItems != null)
                this.SetMenu(menuItems);
            
            // Register tray icons to be removed on application shutdown
            var icons = new Avalonia.Controls.TrayIcons { trayIcon };
            Avalonia.Controls.TrayIcon.SetIcons(Application.Current, icons);

            base.OnFrameworkInitializationCompleted();
        }
    }
}
