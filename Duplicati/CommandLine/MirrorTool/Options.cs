//  Copyright (C) 2014, Kenneth Skovhede

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
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace Duplicati.CommandLine.MirrorTool
{
    public class Options
    {
        public enum SyncDirections
        {
            ToRemote,
            ToLocal,
            BiDirectional
        }

        public enum ConflictPolicies
        {
            ForceRemote,
            ForceLocal,
            ForceNewest,
            KeepLocal,
            KeepRemote,
            KeepNewest
        }

        [OptionAttribute(name: "retries", infoshort: "Number of retries for remote operations", infolong: "Use this option to set the number of retries on a remote operation", defaultvalue: "5")]
        public int Retries { get; private set; }
        [OptionAttribute(name: "verbose", infoshort: "Toogles verbose output", infolong: "Use this option to activate verbose output", defaultvalue: "false")]
        public bool Verbose { get; private set; }
        [OptionAttribute(name: "skip-rename", infoshort: "Avoids renaming", infolong: "Use this option to avoid uploading the file under a different temporary name", defaultvalue: "false")]
        public bool SkipRenaming { get; private set; }
        [OptionAttribute(name: "debug-output", infoshort: "Toggles debug information", infolong: "Use this option to activate debug information, such as stack traces, in the output", defaultvalue: "false")]
        public bool DebugOutput { get; private set; }
        [OptionAttribute(name: "sync-direction", infoshort: "Sets the synchronization direction", infolong: "Use this option to choose how the sync is performed.", defaultvalue: "ToRemote")]
        public SyncDirections SyncDirection { get; private set; }
        [OptionAttribute(name: "conflict-policy", infoshort: "Determines conflict resolution", infolong:"Use this option to set the conflict policy. The \"Keep\" options will create a copy of the conflicting file, where the \"Force\" options will overwrite changes", defaultvalue: "KeepNewest")]
        public ConflictPolicies ConflictPolicy { get; private set; }
        [OptionAttribute(name: "tempfile-prefix", infoshort: "The temporary file prefix", infolong: "To avoid upload issues, files are uploaded to a temporary filename first, and then renamed to the correct name. Use this option to change the name of the temporary files.", defaultvalue: ".tmp-")]
        public string TempFilePrefix { get; private set; }
        [OptionAttribute(name: "dbpath", infoshort: "The database path", infolong: "Use this option to set the path to the database file that keeps track of which files were modified when")]
        public string DbPath { get; private set; }

        private static readonly IDictionary<string, PropertyInfo> PROPMAP;

        static Options()
        {
            PROPMAP = (from n in typeof(Options).GetProperties()
                let attr = (OptionAttribute)n.GetCustomAttributes(typeof(OptionAttribute), false).FirstOrDefault()
                let name = (attr == null || string.IsNullOrWhiteSpace(attr.Name)) ? n.Name.ToLowerInvariant() : attr.Name
                select new KeyValuePair<string, PropertyInfo>(name, n)).ToDictionary(x => x.Key, x => x.Value, StringComparer.InvariantCultureIgnoreCase);
        }

        public static void Print(System.IO.TextWriter output)
        {
            foreach(var n in PROPMAP)
            {
                var oa = n.Value.GetCustomAttributes(typeof(OptionAttribute), true).FirstOrDefault() as OptionAttribute;
                if (oa != null)
                {
                    output.WriteLine("--{0}: {1}", n.Key, oa.InfoShort);
                    output.WriteLine("  {0}", oa.InfoLong);
                    if (!string.IsNullOrWhiteSpace(oa.DefaultValue))
                        output.WriteLine("  default value: {0}", oa.DefaultValue);

                    if (n.Value.PropertyType.IsEnum)
                        output.WriteLine("  valid settings: {0}", string.Join(", ", Enum.GetNames(n.Value.PropertyType)));
                    
                    output.WriteLine();
                }
            }
        }

        public Options(Dictionary<string, string> cmdopts)
        {
            foreach(var n in this.GetType().GetProperties())
            {
                var attr = (OptionAttribute)n.GetCustomAttributes(typeof(OptionAttribute), false).FirstOrDefault();
                if (attr != null && attr.DefaultValue != null)
                    n.SetValue(this, ConvertStringToValue(attr.DefaultValue, n.PropertyType), null);
            }
                    
            foreach(var n in cmdopts)
            {
                PropertyInfo prop;
                if (!PROPMAP.TryGetValue(n.Key, out prop))
                    Duplicati.Library.Logging.Log.WriteMessage(string.Format("Unsupported arg: {0}", n), Duplicati.Library.Logging.LogMessageType.Warning, null);
                else
                    prop.SetValue(this, ConvertStringToValue(n.Value, prop.PropertyType), null);
            }
        }

        private object ConvertStringToValue(string value, Type type)
        {
            if (type == typeof(bool))
                return Duplicati.Library.Utility.Utility.ParseBool(value, true);
            else if (type.IsEnum)
                return Enum.Parse(type, value, true);
            return Convert.ChangeType(value, type);
        }

        public Dictionary<string, string> ToDict()
        {
            return (
                from n in this.GetType().GetProperties()
                let attr = (OptionAttribute)n.GetCustomAttributes(typeof(OptionAttribute), false).FirstOrDefault()
                let name = (attr == null || string.IsNullOrWhiteSpace(attr.Name)) ? n.Name.ToLowerInvariant() : attr.Name
                let raw_val = n.GetValue(this, null)
                let val = raw_val == null ? "" : raw_val.ToString()
                select new KeyValuePair<string, string>(name, val)
            ).ToDictionary(x => x.Key, x => x.Value, StringComparer.InvariantCultureIgnoreCase);
        }
    }
}

