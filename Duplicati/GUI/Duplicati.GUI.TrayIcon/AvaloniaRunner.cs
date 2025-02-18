// Copyright (C) 2025, The Duplicati Team
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Logging;

namespace Duplicati.GUI.TrayIcon
{
    public class AvaloniaRunner : TrayIconBase
    {
        private static readonly string LOGTAG = Log.LogTagFromType<AvaloniaRunner>();
        private AvaloniaApp? application;
        private ProcessBasedActionDelay actionDelayer = new ProcessBasedActionDelay();
        private IEnumerable<AvaloniaMenuItem> menuItems = Enumerable.Empty<AvaloniaMenuItem>();

        public override void Init(string[] args)
        {
            base.Init(args);
        }

        protected override void UpdateUIState(Action action)
            => RunOnUIThread(action);

        internal void RunOnUIThread(Action action)
            => actionDelayer.ExecuteAction(() =>
            {
                try
                {
                    RunOnUIThreadInternal(action);
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "AvaloniaRunOnUIThreadFailed", ex, "Failed to run action on UI thread");
                    throw;
                }
            });

        private void RunOnUIThreadInternal(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Post(action);
        }

        protected override void RegisterStatusUpdateCallback()
        {
            Program.Connection.OnStatusUpdated += this.OnStatusUpdated;
        }

        #region implemented abstract members of Duplicati.GUI.TrayIcon.TrayIconBase
        protected override void Run(string[] args)
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime()
            {
                Args = args,
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            lifetime.Startup += (_, _) => CheckForAppInitialized(lifetime);

            var builder = AppBuilder
                .Configure(() => new AvaloniaApp() { Name = AutoUpdateSettings.AppName })
                .UsePlatformDetect()
                .With(new MacOSPlatformOptions() { ShowInDock = false })
                .SetupWithLifetime(lifetime);

#if DEBUG
            builder = builder.LogToTrace();
#else
            if (Environment.GetEnvironmentVariable("DEBUG_AVALONIA") == "1")
                Logger.Sink = new ConsoleLogSink(LogEventLevel.Information);
            else if (Environment.GetEnvironmentVariable("DEBUG_AVALONIA") == "2")
                Logger.Sink = new ConsoleLogSink(LogEventLevel.Verbose);
#endif

            application = builder.Instance as AvaloniaApp;
            if (application == null)
                throw new InvalidOperationException("Failed to create Avalonia app");
            application.SetMenu(menuItems);
            application.Configure();

            lifetime.Start(args);
        }

        private async void CheckForAppInitialized(IClassicDesktopStyleApplicationLifetime lifetime)
        {
            Exception? lastEx = null;

            // Jump out of the UI thread, ot ensure that the check is done with posting to the UI thread,
            // as opposed to just calling the method directly
            await Task.Delay(100).ConfigureAwait(false);

            // Wait for the app to be initialized, max 5 seconds, after the app has announced it is initialized
            var tcs = new TaskCompletionSource<bool>();
            for (var i = 1; i < 10; i++)
            {
                try
                {
                    // Try a no-op to see if the app is really initialized
                    RunOnUIThreadInternal(() => tcs.TrySetResult(true));
                    await Task.WhenAny(Task.Delay(500), tcs.Task).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }

                if (tcs.Task.IsCompletedSuccessfully)
                    break;

                await Task.Delay(100 * i).ConfigureAwait(false);
            }

            if (tcs.Task.IsCompletedSuccessfully)
            {
                actionDelayer.SignalStart();
            }
            else
            {
                Log.WriteErrorMessage(LOGTAG, "AvaloniaInitFailed", lastEx, "Failed to initialize Avalonia app");
                try
                {
                    lifetime.Shutdown();
                }
                catch (Exception shutdownEx)
                {
                    Log.WriteErrorMessage(LOGTAG, "AvaloniaShutdownFailed", shutdownEx, "Failed to shutdown Avalonia app");
                }
            }
        }

        protected override IMenuItem CreateMenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            return new AvaloniaMenuItem(this, text, icon, callback, subitems);
        }

        public override void NotifyUser(string title, string message, NotificationType type)
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
        }

        protected override void Exit()
        {
            UpdateUIState(() => this.application?.Shutdown());
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
            actionDelayer.Dispose();
        }
        #endregion

        internal static string GetThemePath()
        {
            var isDark = string.Equals(Application.Current?.ActualThemeVariant.ToString(), "Dark", StringComparison.OrdinalIgnoreCase);

            if (OperatingSystem.IsMacOS())
                return isDark ? "macos/dark" : "macos/light";
            if (OperatingSystem.IsWindows())
                return "windows";

            // Linux
            return "linux";
        }


        internal static Bitmap LoadBitmap(string iconName)
        {
            return new Bitmap(AssetLoader.Open(new Uri($"avares://{Assembly.GetExecutingAssembly().FullName}/Assets/icons/{GetThemePath()}/" + iconName)));
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
        public IList<IMenuItem>? SubItems { get; private set; }
        public bool Enabled { get; private set; }
        public MenuIcons Icon { get; private set; }
        public bool IsDefault { get; private set; }
        public bool Hidden { get; private set; }
        private NativeMenuItem? nativeMenuItem;
        private readonly AvaloniaRunner parent;

        public AvaloniaMenuItem(AvaloniaRunner parent, string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            if (subitems != null && subitems.Count > 0)
                throw new NotImplementedException("So far not needed.");

            this.parent = parent;
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
                parent.RunOnUIThread(() => nativeMenuItem.Header = text);
        }

        public void SetIcon(MenuIcons icon)
        {
            this.Icon = icon;
            if (nativeMenuItem != null)
                parent.RunOnUIThread(() => this.UpdateIcon());
        }

        /// <summary>
        /// Performs unguarded update of the icon, ensure calling thread is UI 
        /// and the nativeMenuItem has been set
        /// </summary>
        private void UpdateIcon()
        {
            if (nativeMenuItem == null)
                return;

            nativeMenuItem.Icon = this.Icon switch
            {
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
                parent.RunOnUIThread(() => nativeMenuItem.IsEnabled = this.Enabled);
        }

        public void SetDefault(bool value)
        {
            this.IsDefault = value;
            // Not currently supported by Avalonia, 
            // used to set the menu bold on Windows to indicate the default entry
        }

        public void SetHidden(bool hidden)
        {
            this.Hidden = hidden;
            if (nativeMenuItem != null)
                parent.RunOnUIThread(() => nativeMenuItem.IsVisible = !this.Hidden);

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
        private Avalonia.Controls.TrayIcon? trayIcon;

        private List<AvaloniaMenuItem>? menuItems;

        public override void Initialize()
        {
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
                case TrayIcons.Disconnected:
                    this.trayIcon.Icon = AvaloniaRunner.LoadIcon("normal-disconnected.png");
                    break;
            }
        }

        public void Configure()
        {
            this.Name = Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName;
            if (this.trayIcon != null)
                this.trayIcon.ToolTipText = this.Name;
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
                trayIcon.Clicked -= HandleTrayIconClick;
                trayIcon.Clicked += HandleTrayIconClick;
            }
        }
        
        private readonly ClickDebouncer _clickDebouncer = new ClickDebouncer();

        private void HandleTrayIconClick(object? sender, EventArgs e)
        {
            if (_clickDebouncer.ShouldProcessClick())
            {
                this.menuItems?.FirstOrDefault(x => x.IsDefault)?.Callback();
            }
        }

        public void Shutdown()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Styles.Add(new FluentTheme());

            var icon = AvaloniaRunner.LoadIcon("normal.png");
            this.trayIcon = new Avalonia.Controls.TrayIcon() { Icon = icon };

            // Handle being loaded with menu items
            if (menuItems != null)
                this.SetMenu(menuItems);

            // Register tray icons to be removed on application shutdown
            var icons = new Avalonia.Controls.TrayIcons { trayIcon };
            Avalonia.Controls.TrayIcon.SetIcons(this, icons);

            base.OnFrameworkInitializationCompleted();
        }
    }

    internal class ConsoleLogSink(LogEventLevel minLevel) : ILogSink
    {
        private readonly LogEventLevel _minLevel = minLevel;

        public bool IsEnabled(LogEventLevel level, string area)
            => level >= _minLevel;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
            => Log(level, area, source, messageTemplate, []);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
            => Console.WriteLine($"Avalonia [{level}]: {source} {messageTemplate} {string.Join(" ", propertyValues)}");
    }

}
