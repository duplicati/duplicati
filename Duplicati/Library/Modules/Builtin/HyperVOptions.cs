#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.Modules.Builtin
{
    public class HyperVOptions : Interface.IGenericSourceModule
    {
        private const string OPTION_SOURCE = "hyperv-backup-vm";
        private const string m_HyperVPathGuidRegExp = @"\%HYPERV\%\\(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}";
        private const string m_HyperVGuidRegExp = @"(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}";
        private const string m_HyperVPathAllRegExp = @"%HYPERV%";

        #region IGenericModule Members

        public string Key
        {
            get { return "hyperv-options"; }
        }

        public string DisplayName
        {
            get { return Strings.HyperVOptions.DisplayName; }
        }

        public string Description
        {
            get { return Strings.HyperVOptions.Description; }
        }

        public bool LoadAsDefault
        {
            get { return true; }
        }

        public IList<Interface.ICommandLineArgument> SupportedCommands
        {
            get { return null; }
        }

        public IList<Interface.ICommandLineArgument> HiddenCommands
        {
            get {
                return new List<Duplicati.Library.Interface.ICommandLineArgument>(new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_SOURCE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, "", "")
                });
            }
        }

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
        }

        #endregion
        
        #region Implementation of IGenericSourceModule
        public Dictionary<string, string> ParseSource(ref string[] paths, ref string filter, Dictionary<string, string> commandlineOptions)
        {
            var pathshyperv = new List<string>();
            var ret = new Dictionary<string, string>();

            if (paths.Contains(m_HyperVPathAllRegExp, StringComparer.OrdinalIgnoreCase))
            {
                if (Utility.Utility.IsClientWindows)
                    pathshyperv = new HyperVUtility().GetHyperVGuests().Select(x => string.Format(@"%HYPERV%\{0}", x.ID)).ToList();
            }
            else
                pathshyperv = paths.Where(x => Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToList();

            paths = paths.Where(x => !x.Equals(m_HyperVPathAllRegExp, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
            
            if (!string.IsNullOrEmpty(filter))
            {
                var filters = filter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);

                var filtersInclude = filters.Where(x => x.StartsWith("+") && Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Select(x => x.Substring(1)).ToList();
                var filtersExclude = filters.Where(x => x.StartsWith("-") && Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Select(x => x.Substring(1)).ToList();

                var remainingfilters = filters.Where(x => !Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase)).ToArray();
                filter = string.Join(System.IO.Path.PathSeparator.ToString(), remainingfilters);

                pathshyperv = pathshyperv.Union(filtersInclude).Except(filtersExclude).ToList();
            }

            pathshyperv = pathshyperv.Select(x => Regex.Match(x, m_HyperVGuidRegExp).Value).ToList();

            if (pathshyperv.Count != 0 && Utility.Utility.IsClientWindows && (!commandlineOptions.Keys.Contains("snapshot-policy") || commandlineOptions["snapshot-policy"] != "required"))
            {
                Logging.Log.WriteMessage("Snapshot strategy have to be set to \"required\" when backuping Hyper-V virtual machines. Changing to \"required\" to continue.", Logging.LogMessageType.Information);
                ret["snapshot-policy"] = "required";
            }

            if (pathshyperv.Count != 0)
            {
                if (Utility.Utility.IsClientWindows)
                    ret[OPTION_SOURCE] = string.Join(System.IO.Path.PathSeparator.ToString(), pathshyperv);
                else
                    Logging.Log.WriteMessage("Hyper-V backup works only on Windows OS.", Logging.LogMessageType.Warning);
            }

            return ret;
        }
        
        public bool ContainFiles(Dictionary<string, string> commandlineOptions)
        {
            if (commandlineOptions != null && !commandlineOptions.Keys.Contains(OPTION_SOURCE))
                return false;

            if (commandlineOptions[OPTION_SOURCE].Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries).Count() > 0)
                return true;
            else
                return false;
        }

        #endregion
    }
}
