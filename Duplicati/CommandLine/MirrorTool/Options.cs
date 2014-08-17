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

        [OptionAttribute(name: "retries")]
        public int Retries { get; private set; }
        [OptionAttribute(name: "verbose")]
        public bool Verbose { get; private set; }
        [OptionAttribute(name: "debug-output")]
        public bool DebugOutput { get; private set; }
        [OptionAttribute(name: "sync-direction", infoshort: "", infolong: "", defaultvalue: "BiDirectional")]
        public SyncDirections SyncDirection { get; private set; }
        [OptionAttribute(name: "conflict-policy", infoshort:"", infolong:"", defaultvalue: "KeepNewest")]
        public ConflictPolicies ConflictPolicy { get; private set; }
        [OptionAttribute(name: "tempfile-prefix", infoshort: "", infolong: "", defaultvalue: ".tmp-")]
        public string TempFilePrefix { get; private set; }
        [OptionAttribute(name: "dbpath")]
        public string DbPath { get; private set; }


        private static readonly IDictionary<string, PropertyInfo> PROPMAP;

        static Options()
        {
            PROPMAP = (from n in typeof(Options).GetProperties()
                let attr = (OptionAttribute)n.GetCustomAttributes(typeof(OptionAttribute), false).FirstOrDefault()
                let name = (attr == null || string.IsNullOrWhiteSpace(attr.Name)) ? n.Name.ToLowerInvariant() : attr.Name
                select new KeyValuePair<string, PropertyInfo>(name, n)).ToDictionary(x => x.Key, x => x.Value, StringComparer.InvariantCultureIgnoreCase);
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

