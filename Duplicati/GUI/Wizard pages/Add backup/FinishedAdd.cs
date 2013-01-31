#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class FinishedAdd : WizardControl
    {
        WizardSettingsWrapper m_wrapper;

        public FinishedAdd()
            : base(Strings.FinishedAdd.PageTitle, Strings.FinishedAdd.PageDescription)
        {
            InitializeComponent();

            MonoSupport.FixTextBoxes(this);

            base.PageEnter += new PageChangeHandler(FinishedAdd_PageEnter);
            base.PageLeave += new PageChangeHandler(FinishedAdd_PageLeave);
        }

        void FinishedAdd_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            m_wrapper.RunImmediately = RunNow.Checked;
        }

        void FinishedAdd_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            List<KeyValuePair<string, string>> strings = new List<KeyValuePair<string, string>>();
            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
                strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryAction, Strings.FinishedAdd.SummaryActionAdd));
            else
                strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryAction, Strings.FinishedAdd.SummaryActionModify));

            strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummarySourceFolder, m_wrapper.SourcePath));
            strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryWhen, m_wrapper.BackupTimeOffset.ToString()));
            if (!string.IsNullOrEmpty(m_wrapper.RepeatInterval))
                strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryRepeat, m_wrapper.RepeatInterval));
            if (!string.IsNullOrEmpty(m_wrapper.FullBackupInterval))
                strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryFullBackupEach, m_wrapper.FullBackupInterval));
            if (m_wrapper.MaxFullBackups > 0)
                strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryKeepFullBackups, m_wrapper.MaxFullBackups.ToString()));

            strings.Add(new KeyValuePair<string, string>(null, null));
            strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryDestination, m_wrapper.Backend.ToString()));

            //TODO: Figure out how to make a summary

            /*switch(m_wrapper.Backend)
            {
                case WizardSettingsWrapper.BackendType.File:
                    FileSettings file = new FileSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryDestinationPath, file.Path));
                    break;
                case WizardSettingsWrapper.BackendType.FTP:
                    FTPSettings ftp = new FTPSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryDestinationPath, ftp.Server + "/" + ftp.Path));
                    break;
                case WizardSettingsWrapper.BackendType.SSH:
                    SSHSettings ssh = new SSHSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryDestinationPath, ssh.Server + "/" + ssh.Path));
                    break;
                case WizardSettingsWrapper.BackendType.S3:
                    S3Settings s3 = new S3Settings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryDestinationPath, s3.Path));
                    break;
                case WizardSettingsWrapper.BackendType.WebDav:
                    WEBDAVSettings webdav = new WEBDAVSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedAdd.SummaryDestinationPath, webdav.Path));
                    break;
            }*/
            
            int maxlen = 0;
            foreach (KeyValuePair<string, string> i in strings)
                if (i.Key != null)
                    maxlen = Math.Max(maxlen, i.Key.Length);

            System.Text.StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> i in strings)
                if (i.Key == null)
                    sb.Append("\r\n");
                else
                    sb.Append(i.Key + ": " + new String(' ', maxlen - i.Key.Length) + i.Value + "\r\n");

            Summary.Text = sb.ToString();

            args.TreatAsLast = true;

            CommandLine.Text = "";
            Tabs.SelectedTab = TabSummary;
        }

        public override string HelpText
        {
            get
            {
                if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
                    return base.HelpText;
                else
                    return Strings.FinishedAdd.PageDescriptionModify;
            }
        }

        public override string Title
        {
            get
            {
                if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
                    return base.Title;
                else
                    return Strings.FinishedAdd.PageTitleModify;
            }
        }

        private string QuoteIfRequired(string input)
        {
            if (Library.Utility.Utility.IsClientLinux)
            {
                input = input.Replace("\\", "\\\\")
                             .Replace("$", "\\$")
                             .Replace("*", "\\*")
                             .Replace("?", "\\?")
                             .Replace("`", "\\`");
            }

            if (input.IndexOfAny(new char[] { ' ', '|', '&' }) >= 0)
            {
                if (!Library.Utility.Utility.IsClientLinux && input.EndsWith("\\"))
                    input += "\\";
                return '"' + input + '"';
            }
            return input;
        }

        private void Tabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            // The CommandLine box is filled only on demand.

            if (Tabs.SelectedTab == TabCommandLine && CommandLine.TextLength == 0)
            {
                // Getting all options for the task
                System.Data.LightDatamodel.IDataFetcherWithRelations con = new System.Data.LightDatamodel.DataFetcherNested(Program.DataConnection);
                Schedule schedule = con.Add<Schedule>();
                m_wrapper.UpdateSchedule(schedule);
                Dictionary<string, string> options = new Dictionary<string, string>();
                String target = new IncrementalBackupTask(schedule).GetConfiguration(options);

                List<Library.Interface.ICommandLineArgument> defaults = SettingOverrides.GetModuleOptions(m_wrapper, null);
                defaults.AddRange(new Library.Main.Options(new Dictionary<string, string>()).SupportedCommands);

                String source = schedule.Task.SourcePath;
                if (source == "")
                {
                    // We need to extract the paths of the "easy" folders                    
                    string[] sourceFolders = DynamicSetupHelper.GetSourceFolders(schedule.Task, new ApplicationSettings(schedule.Task.DataParent), new List<KeyValuePair<bool, string>>());
                    source = String.Join(System.IO.Path.PathSeparator.ToString(), sourceFolders);
                }
                
                // We have everything needed to build the Command line
                System.Text.StringBuilder sb = new StringBuilder();
                sb.AppendLine("Command-line equivalent of this task:");
                sb.AppendLine();
                sb.Append(QuoteIfRequired(Application.ExecutablePath.Substring(0, Application.ExecutablePath.Length - 4) + ".CommandLine.exe"));
                sb.Append(" backup");
                
                // Filters are handled differently
                options.Remove("filter");

                //Remove all default values, and mask passwords
                foreach (Library.Interface.ICommandLineArgument arg in defaults)
                {
                    string defvalue = arg.DefaultValue == null ? "" : arg.DefaultValue;
                    List<string> names = new List<string>();
                    names.Add(arg.Name);
                    if (arg.Aliases != null)
                        names.AddRange(arg.Aliases);

                    foreach(string a in names)
                    {
                        if (options.ContainsKey(a))
                            switch(arg.Type)
                            {
                                case Library.Interface.CommandLineArgument.ArgumentType.Password:
                                    if (!string.IsNullOrEmpty(options[a]))
                                        options[a] = "**********";
                                    break;

                                case Library.Interface.CommandLineArgument.ArgumentType.Enumeration:
                                case Library.Interface.CommandLineArgument.ArgumentType.Size:
                                    if (string.Equals(defvalue, options[a] == null ? "" : options[a], StringComparison.InvariantCultureIgnoreCase))
                                        options.Remove(a);
                                    break;

                                case Library.Interface.CommandLineArgument.ArgumentType.Path:
                                    if (string.Equals(defvalue, options[a] == null ? "" : options[a], Library.Utility.Utility.ClientFilenameStringComparision))
                                        options.Remove(a);
                                    break;

                                case Library.Interface.CommandLineArgument.ArgumentType.Boolean:
                                    bool parsed = Library.Utility.Utility.ParseBoolOption(options, a);
                                    bool defbool = Library.Utility.Utility.ParseBool(defvalue, false);
                                    if (parsed == defbool)
                                        options.Remove(a);

                                    break;

                                default:
                                    if (string.Equals(defvalue, options[a] == null ? "" : options[a]))
                                        options.Remove(a);
                                    break;
                            }
                    }
                }

                foreach (KeyValuePair<string, string> i in options)
                {
                    sb.Append(" --" + i.Key);
                    if (!string.IsNullOrEmpty(i.Value))
                        sb.Append("=" + QuoteIfRequired(i.Value));
                }

                // Filters
                foreach (Datamodel.TaskFilter filter in schedule.Task.SortedFilters)
                {
                    sb.Append(" --");
                    sb.Append(filter.Include ? "include" : "exclude");
                    sb.Append(filter.GlobbingFilter.Length == 0 ? "-regexp" : "");
                    sb.Append("=");
                    sb.Append(QuoteIfRequired(filter.GlobbingFilter.Length > 0 ? filter.GlobbingFilter : filter.Filter));
                }

                // Source and target
                sb.Append(" " + QuoteIfRequired(source));
                sb.Append(" " + QuoteIfRequired(target));

                // Cleanup commands do not need encryption passphrase
                options.Remove("passphrase");

                if (!string.IsNullOrEmpty(m_wrapper.BackupExpireInterval))
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.Append(QuoteIfRequired(Application.ExecutablePath.Substring(0, Application.ExecutablePath.Length - 4) + ".CommandLine.exe"));
                    sb.Append(" delete-older-than ");
                    sb.Append(QuoteIfRequired(m_wrapper.BackupExpireInterval));

                    foreach (KeyValuePair<string, string> i in options)
                    {
                        sb.Append(" --" + i.Key);
                        if (!string.IsNullOrEmpty(i.Value))
                            sb.Append("=" + QuoteIfRequired(i.Value));
                    }

                    sb.Append(" " + QuoteIfRequired(target));
                }

                if (m_wrapper.MaxFullBackups > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.Append(QuoteIfRequired(Application.ExecutablePath.Substring(0, Application.ExecutablePath.Length - 4) + ".CommandLine.exe"));
                    sb.Append(" delete-all-but-n");
                    sb.Append(" " + QuoteIfRequired(m_wrapper.MaxFullBackups.ToString()));

                    foreach (KeyValuePair<string, string> i in options)
                    {
                        sb.Append(" --" + i.Key);
                        if (!string.IsNullOrEmpty(i.Value))
                            sb.Append("=" + QuoteIfRequired(i.Value));
                    }
                    
                    sb.Append(" " + QuoteIfRequired(target));
                }

                CommandLine.Text = sb.ToString();
            }
        }

    }
}
