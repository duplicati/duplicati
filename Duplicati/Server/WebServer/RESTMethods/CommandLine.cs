//  Copyright (C) 2017, The Duplicati Team
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
using System.IO;
using System.Linq;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class CommandLine : IRESTMethodGET, IRESTMethodPOST
    {
        private readonly Dictionary<string, System.Reflection.MethodInfo> m_supportedCommands;

        private class ArgumentCountAttribute : Attribute
        {
            public int Min = -1;
            public int Max = -1;
        }

        private class DocumentationAttribute : Attribute
        {
            public string Key;
            public string Description;
        }

        private class ParsedResults
        {
            public List<string> Arguments;
            public Dictionary<string, string> Options;
            public Library.Utility.IFilter Filter;

            public ParsedResults(RequestInfo info)
            {
                using (var sr = new StreamReader(info.Request.Body, System.Text.Encoding.UTF8, true))
                    Arguments = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(sr.ReadToEnd());

                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(Arguments);
                Options = tmpparsed.Item1;
                Filter = tmpparsed.Item2;
            }
        }

        public CommandLine()
        {
            m_supportedCommands = new Dictionary<string, System.Reflection.MethodInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in this.GetType().GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                foreach (var attr in m.GetCustomAttributes(typeof(DocumentationAttribute), true).Cast<DocumentationAttribute>())
                    m_supportedCommands[attr.Key] = m;
        }

        [ArgumentCount(Min = 0, Max = 0)]
        [Documentation(Key = "System-Info", Description = "Lists various system properties")]
        private Duplicati.Library.Interface.IBasicResults System_Info(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller("dummy://", pr.Options, null))
                return controller.SystemInfo();
        }

        [ArgumentCount(Min = 1)]
        [Documentation(Key = "Find", Description = "Finds files in a backup")]
        private Duplicati.Library.Interface.IBasicResults Find(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.List(pr.Arguments.Skip(1), pr.Filter);
        }

        [ArgumentCount(Min = 1, Max = 1)]
        [Documentation(Key = "List", Description = "Lists all remote backup sets")]
        private Duplicati.Library.Interface.IBasicResults List(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.List(pr.Arguments.Skip(1), pr.Filter);
        }

        [ArgumentCount(Min = 1, Max = 1)]
        [Documentation(Key = "Delete", Description = "Deletes specific remote backup sets")]
        private Duplicati.Library.Interface.IBasicResults Delete(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.Delete();
        }

        [ArgumentCount(Min = 1, Max = 1)]
        [Documentation(Key = "Repair", Description = "Performs a repair of the remote data")]
        private Duplicati.Library.Interface.IBasicResults Repair(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.Repair(pr.Filter);
        }

        [ArgumentCount(Min = 2)]
        [Documentation(Key = "Restore-Control-Files", Description = "Restores control files found in the remote backup")]
        private Duplicati.Library.Interface.IBasicResults Restore_Control_Files(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.RestoreControlFiles(pr.Arguments.Skip(1), pr.Filter);
        }

        [ArgumentCount(Min = 1)]
        [Documentation(Key = "List-Control-Files", Description = "Lists control files found in the remote backup")]
        private Duplicati.Library.Interface.IBasicResults List_Control_Files(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.ListControlFiles(pr.Arguments.Skip(1), pr.Filter);
        }

        [ArgumentCount(Min = 1, Max = 1)]
        [Documentation(Key = "List-Remote", Description = "Lists files in the remote folder")]
        private Duplicati.Library.Interface.IBasicResults List_Remote(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.ListRemote();
        }

        [ArgumentCount(Min = 1, Max = 1)]
        [Documentation(Key = "Compact", Description = "Performs a compact procedure")]
        private Duplicati.Library.Interface.IBasicResults Compact(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.Compact();
        }

        [ArgumentCount(Min = 2, Max = 2)]
        [Documentation(Key = "Recreate-Database", Description = "Rebuilds the local database from remote data")]
        private Duplicati.Library.Interface.IBasicResults Recreate_Database(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.RecreateDatabase(pr.Arguments.Skip(1).First(), pr.Filter);
        }

        [ArgumentCount(Min = 2, Max = 2)]
        [Documentation(Key = "Create-Bug-Report", Description = "Creates a bug-report database")]
        private Duplicati.Library.Interface.IBasicResults Create_Log_Database(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.CreateLogDatabase(pr.Arguments.Skip(1).First());
        }

        [ArgumentCount(Min = 3)]
        [Documentation(Key = "List-Changes", Description = "Lists changes between two versions")]
        private Duplicati.Library.Interface.IBasicResults List_Changes(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.ListChanges(pr.Arguments.Skip(1).First(), pr.Arguments.Skip(2).First(), pr.Arguments.Skip(3), pr.Filter);
        }

        [ArgumentCount(Min = 2)]
        [Documentation(Key = "Affected", Description = "Lists files affected by a named remote volume")]
        private Duplicati.Library.Interface.IBasicResults List_Affected(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.ListAffected(pr.Arguments.Skip(1).ToList());
        }

        [ArgumentCount(Min = 1, Max = 2)]
        [Documentation(Key = "Test", Description = "Performs verification on the remote files")]
        private Duplicati.Library.Interface.IBasicResults Test(ParsedResults pr)
        {
            long samples = 1;
            if (pr.Arguments.Count > 1)
                samples = long.Parse(pr.Arguments[2]);
            
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.Test(samples);
        }

        [ArgumentCount(Min = 2)]
        [Documentation(Key = "Test-Filter", Description = "Tests filters")]
        private Duplicati.Library.Interface.IBasicResults Test_Filter(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.TestFilter(pr.Arguments.Skip(1).ToArray(), pr.Filter);
        }

        [ArgumentCount(Min = 2)]
        [Documentation(Key = "Backup", Description = "Runs the backup operation")]
        private Duplicati.Library.Interface.IBasicResults Backup(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.TestFilter(pr.Arguments.Skip(1).ToArray(), pr.Filter);
        }

        [ArgumentCount(Min = 1, Max = 1)]
        [Documentation(Key = "Purge", Description = "Purges named files from remote filesets")]
        private Duplicati.Library.Interface.IBasicResults Purge_Files(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.PurgeFiles(pr.Filter);
        }

        [ArgumentCount(Min = 1, Max = 1)]
        [Documentation(Key = "List-Broken-Files", Description = "Lists files that cannot be recovered due to broken or missing remote files")]
        private Duplicati.Library.Interface.IBasicResults List_Broken_Files(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.ListBrokenFiles(pr.Filter);
        }

        [ArgumentCount(Min = 1, Max = 1)]
        [Documentation(Key = "Purge-Broken-Files", Description = "Removes files that cannot be restored from the remote, due to missing or broken files")]
        private Duplicati.Library.Interface.IBasicResults Purge_Broken_Files(ParsedResults pr)
        {
            using (var controller = new Library.Main.Controller(pr.Arguments.First(), pr.Options, null))
                return controller.ListBrokenFiles(pr.Filter);
        }

        public void POST(string key, RequestInfo info)
        {
            var pr = new ParsedResults(info);

            if (string.IsNullOrWhiteSpace(key) && pr.Arguments.Count > 0)
            {
                key = pr.Arguments[0];
                pr.Arguments.RemoveAt(0);
            }

            System.Reflection.MethodInfo method;
            if (!m_supportedCommands.TryGetValue(key ?? string.Empty, out method))
            {
                info.ReportClientError("Not Found", System.Net.HttpStatusCode.NotFound);
                return;
            }

            var limits = method.GetCustomAttributes(typeof(ArgumentCountAttribute), true).Cast<ArgumentCountAttribute>().FirstOrDefault();
            if (limits != null)
            {
                if (limits.Min >= 0 && pr.Arguments.Count < limits.Min)
                {
                    info.ReportClientError("Too few arguments");
                    return;
                }

                if (limits.Max >= 0 && pr.Arguments.Count > limits.Max)
                {
                    info.ReportClientError("Too many arguments");
                    return;
                }
            }

            try
            {
                var res = method.Invoke(this, new object[] { pr });
                info.OutputOK(res.ToString());
            }
            catch (Exception ex)
            {
                var rx = ex;
                if (rx is System.Reflection.TargetInvocationException)
                    rx = rx.InnerException;
                
                if (rx is Library.Interface.UserInformationException)
                {
                    info.OutputError(rx.Message, System.Net.HttpStatusCode.BadRequest, rx.Message);
                    return;
                }

                throw rx;
            }
        }

        public void GET(string key, RequestInfo info)
        {
            info.OutputOK(
                m_supportedCommands
                .Values
                .SelectMany(
                    x =>
                        x.GetCustomAttributes(typeof(DocumentationAttribute), true)
                        .Cast<DocumentationAttribute>()
                        .Select(y => new { Method = x, Attr = y })
                )
                .Select(
                    x =>
                        new 
                        {
                            Key = x.Attr.Key,
                            Description = x.Attr.Description,
                        }
                )
            );
                
        }
    }
}
