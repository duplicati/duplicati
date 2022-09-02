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


            Avalonia.Controls.AppBuilderBase<Avalonia.AppBuilder>.Configure<AvaloniaApp>()
                .UsePlatformDetect()
                .LogToTrace().StartWithClassicDesktopLifetime(args);
        }

        protected override IMenuItem CreateMenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            return new Win32MenuItem(text, icon, callback, subitems);
        }

        protected override void NotifyUser(string title, string message, NotificationType type)
        {
            var icon = Win32NativeNotifyIcon.InfoFlags.NIIF_INFO;

            switch (type)
            {
                case NotificationType.Information:
                    icon = Win32NativeNotifyIcon.InfoFlags.NIIF_INFO;
                    break;
                case NotificationType.Warning:
                    icon = Win32NativeNotifyIcon.InfoFlags.NIIF_WARNING;
                    break;
                case NotificationType.Error:
                    icon = Win32NativeNotifyIcon.InfoFlags.NIIF_ERROR;
                    break;
            }

            //m_ntfIcon.ShowBalloonTip(title, message, icon);
        }

        protected override void Exit()
        {
            //m_ntfIcon.Delete();
            //m_window.DestroyWindow();
        }

        protected override void SetIcon(TrayIcons icon)
        {
            //There are calls before NotifyIcons is created
            //if (m_ntfIcon == null)
            //    return;

            //switch (icon)
            //{
            //    case TrayIcons.IdleError:
            //        m_ntfIcon?.SetIcon(Win32IconLoader.TrayErrorIcon);
            //        break;
            //    case TrayIcons.Paused:
            //    case TrayIcons.PausedError:
            //        m_ntfIcon?.SetIcon(Win32IconLoader.TrayPauseIcon);
            //        break;
            //    case TrayIcons.Running:
            //    case TrayIcons.RunningError:
            //        m_ntfIcon?.SetIcon(Win32IconLoader.TrayWorkingIcon);
            //        break;
            //    case TrayIcons.Idle:
            //        m_ntfIcon?.SetIcon(Win32IconLoader.TrayNormalIcon);
            //        break;
            //    default:
            //        m_ntfIcon?.SetIcon(Win32IconLoader.TrayNormalIcon);
            //        break;
            //}
        }

        protected override void SetMenu(System.Collections.Generic.IEnumerable<IMenuItem> items)
        {
           //m_TrayContextMenu = new List<Win32MenuItem>();

            //foreach(var item in items)
            //    m_TrayContextMenu.Add((Win32MenuItem)item);
        }

        public override void Dispose()
        {
        }
        #endregion
    }

    public class AvaloniaApp : Application
    {
        public override void Initialize()
        {
            //AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var light = new FluentTheme(new Uri($"avares://{Assembly.GetExecutingAssembly().GetName()}")) { Mode = FluentThemeMode.Light };
                Styles.Add(light);

                var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();
                var bitmap = new Bitmap(assets.Open(new Uri($"avares://{Assembly.GetExecutingAssembly().FullName}/Assets/icons/icon.png")));
                var icon = new WindowIcon(bitmap);
                var trayIcon = new Avalonia.Controls.TrayIcon();
                trayIcon.Icon = icon; //desktop.MainWindow.Icon;
                trayIcon.ToolTipText = "Test";
                var menu = new NativeMenu();
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
