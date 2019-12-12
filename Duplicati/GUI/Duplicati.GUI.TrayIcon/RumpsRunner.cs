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
using Newtonsoft.Json;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Common;

namespace Duplicati.GUI.TrayIcon
{
    public class RumpsRunner : TrayIconBase
    {
        private class MenuItemWrapper : Duplicati.GUI.TrayIcon.IMenuItem
        {

            private readonly RumpsRunner m_parent;

            public bool Default { get; private set; }
            public bool Enabled { get; private set; }
            public MenuIcons Icon { get; private set; }
            public string Text { get; private set; }

            public MenuItemWrapper(RumpsRunner parent, string text, Action callback, IList<Duplicati.GUI.TrayIcon.IMenuItem> subitems)
            {
                m_parent = parent;
                Key = Guid.NewGuid().ToString("N");
                this.Text = text ?? "";
                Callback = callback;
                this.Enabled = true;
                this.Default = false;
                if (subitems != null)
                    Subitems = subitems.Cast<MenuItemWrapper>().ToList();
            }

            [JsonIgnore]
            public Action Callback { get; private set; }

            public string Key { get; private set; }

            public IList<MenuItemWrapper> Subitems { get; private set; }
            
            #region IMenuItem implementation
            public void SetText(string text)
            {
                if (this.Text != text)
                {
                    this.Text = text;
                    m_parent.UpdateMenu(this);
                }
            }

            public void SetIcon(MenuIcons icon)
            {
                if (this.Icon != icon)
                {
                    this.Icon = icon;
                    m_parent.UpdateMenu(this);
                }
            }

            public void SetDefault(bool isDefault)
            {
                if (this.Default != isDefault)
                {
                    this.Default = isDefault;
                    m_parent.UpdateMenu(this);
                }
            }
            #endregion
        }

        private static readonly System.Reflection.Assembly ASSEMBLY = System.Reflection.Assembly.GetExecutingAssembly();
        private static readonly string ICON_PATH = ASSEMBLY.GetName().Name + ".OSX_Icons.";

        private static readonly string ICON_NORMAL = ICON_PATH + "normal.png";
        private static readonly string ICON_PAUSED = ICON_PATH + "normal-pause.png";
        private static readonly string ICON_RUNNING = ICON_PATH + "normal-running.png";
        private static readonly string ICON_WARNING = ICON_PATH + "normal-error.png"; // TODO: create a normal-warning.png, for now use normal-error.png
        private static readonly string ICON_ERROR = ICON_PATH + "normal-error.png";

        private readonly Dictionary<Duplicati.GUI.TrayIcon.TrayIcons, string> m_images = new Dictionary<Duplicati.GUI.TrayIcon.TrayIcons, string>();

        private System.Diagnostics.Process m_rumpsProcess;

        private static readonly string RUMPS_PYTHON = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RUMPS_PYTHON")) ? "/usr/bin/python2.7" : Environment.GetEnvironmentVariable("RUMPS_PYTHON");

        private static readonly string SCRIPT_PATH = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "OSXTrayHost", "osx-trayicon-rumps.py");
        private IsolatedChannelScope m_scope;
        private IWriteChannelEnd<string> m_toRumps;
        private List<MenuItemWrapper> m_menus;
        private bool m_isQuitting = false;
        private TrayIcons m_lastIcon;

        public static bool CanRun()
        {
            if (!Platform.IsClientOSX)
                return false;
            
            if (!File.Exists(SCRIPT_PATH) || !File.Exists(RUMPS_PYTHON))
                return false;

            try
            {
                var si = System.Diagnostics.Process.Start(RUMPS_PYTHON, string.Format("\"{0}\" TEST", SCRIPT_PATH));
                si.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
                if (!si.HasExited)
                {
                    si.Kill();
                    return false;
                }

                return si.ExitCode == 0;
            }
            catch
            {
            }

            return false;
        }

        private void ResetMenus()
        {
            m_toRumps.WriteNoWait(JsonConvert.SerializeObject(new {Action = "setmenus", Menus = m_menus}));
        }

        private void Restart()
        {
            if (m_toRumps != null)
                m_toRumps.Dispose();
            
            var startinfo = new System.Diagnostics.ProcessStartInfo(RUMPS_PYTHON, string.Format("\"{0}\"", SCRIPT_PATH));
            startinfo.CreateNoWindow = true;
            startinfo.UseShellExecute = false;
            startinfo.RedirectStandardInput = true;
            startinfo.RedirectStandardOutput = true;
            startinfo.RedirectStandardError = true;

            m_rumpsProcess = System.Diagnostics.Process.Start(startinfo);
            var ch = ChannelManager.CreateChannel<string>();
            m_toRumps = ch.AsWriteOnly();

            WriteChannel(m_rumpsProcess.StandardInput, ch.AsReadOnly());
            #pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            ReadChannel(m_rumpsProcess.StandardOutput);
            ReadChannel(m_rumpsProcess.StandardError);
            #pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            m_toRumps.WriteNoWait(JsonConvert.SerializeObject(new {Action = "background"}));
            //m_toRumps.WriteNoWait(JsonConvert.SerializeObject(new {Action = "setappicon", Image = GetIcon(m_lastIcon)}));

            if (m_menus != null)
            {
                ResetMenus();
                m_toRumps.WriteNoWait(JsonConvert.SerializeObject(new {Action = "seticon", Image = GetIcon(m_lastIcon)}));
            }
        }

        public override void Init(string[] args)
        {
            m_scope = new IsolatedChannelScope();
            Restart();

            base.Init(args);
        }

        private static Task WriteChannel(StreamWriter stream, IReadChannelEnd<string> ch)
        {
            stream.AutoFlush = true;

            return AutomationExtensions.RunTask(
                new {
                    Input = ch
                },
                async self =>
                {
                    using(stream)
                    {
                        while(true)
                        {
                            var line = await self.Input.ReadAsync();
                            await stream.WriteLineAsync(line).ConfigureAwait(false);
                            //Console.WriteLine("Wrote {0}", line);
                        }
                    }
                }
            );
        }

        private async Task ReadChannel(StreamReader stream)
        {
            string line;
            using(stream)
                while ((line = await stream.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    //Console.WriteLine("Got message: {0}", line);

                    line = line.Trim();
                    if (line.StartsWith("click:", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = line.Substring("click:".Length);
                        var menu = m_menus.FirstOrDefault(x => string.Equals(x.Key, key));
                        if (menu == null)
                        {
                            Console.WriteLine("Menu not found: {0}", key);
                        }
                        else
                        {
                            menu.Callback();
                        }
                    }
                    else if (!line.StartsWith("info", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(line))
                    {
                        Console.WriteLine("Unexpected message: {0}", line);
                    }
                            
                }
        }

        private void UpdateMenu(MenuItemWrapper menu)
        {
            if (m_menus != null)
                m_toRumps.WriteNoWait(JsonConvert.SerializeObject(new {Action = "setmenu", Menu = menu}));
        }

        private string LoadStream(System.IO.Stream s)
        {
            using(var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        protected override void Run(string[] args)
        {
            m_rumpsProcess.WaitForExit();
            while(!m_isQuitting)
            {
                Restart();
                m_rumpsProcess.WaitForExit();
            }
        }

        protected override void UpdateUIState(Action action)
        {
            action();
        }

        private string GetIcon(Duplicati.GUI.TrayIcon.TrayIcons icon)
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

        protected override Duplicati.GUI.TrayIcon.IMenuItem CreateMenuItem (string text, Duplicati.GUI.TrayIcon.MenuIcons icon, Action callback, System.Collections.Generic.IList<Duplicati.GUI.TrayIcon.IMenuItem> subitems)
        {
            return new MenuItemWrapper(this, text, callback, subitems);
        }

        protected override void Exit()
        {
            m_isQuitting = true;
            if (m_rumpsProcess != null && !m_rumpsProcess.HasExited)
            {
                if (m_toRumps != null)
                {
                    m_toRumps.WriteNoWait(JsonConvert.SerializeObject(new { Action = "shutdown" }));
                    m_toRumps.Dispose();
                    m_toRumps = null;
                }

                m_rumpsProcess.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
                m_rumpsProcess.Kill();
                m_rumpsProcess = null;
            }

        }

        protected override void SetIcon(TrayIcons icon)
        {
            m_lastIcon = icon;
            m_toRumps.WriteNoWait(JsonConvert.SerializeObject(new { Action = "seticon", Image = GetIcon(icon) }));
        }

        protected override void SetMenu(System.Collections.Generic.IEnumerable<Duplicati.GUI.TrayIcon.IMenuItem> items)
        {
            m_menus = items.Cast<MenuItemWrapper>().ToList();

            ResetMenus();
        }

        protected override void NotifyUser(string title, string message, NotificationType type)
        {
            m_toRumps.WriteNoWait(JsonConvert.SerializeObject(new {Action = "notification", Title = title, Message = message}));
        }

        public override void Dispose()
        {
            if (m_scope != null)
                m_scope.Dispose();
        }
    }
}

