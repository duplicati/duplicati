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

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
        }

        #endregion
        
        #region Implementation of IGenericSourceModule
        public Dictionary<string, string> ParseSourcePaths(ref string[] paths, ref string filter, Dictionary<string, string> commandlineOptions)
        {
            var changedOptions = new Dictionary<string, string>();
            var filtersInclude = new List<string>();
            var filtersExclude = new List<string>();

            if (!string.IsNullOrEmpty(filter))
            {
                var filters = filter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);

                filtersInclude = filters.Where(x => x.StartsWith("+") && Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Select(x => Regex.Match(x.Substring(1), m_HyperVGuidRegExp).Value).ToList();
                filtersExclude = filters.Where(x => x.StartsWith("-") && Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Select(x => Regex.Match(x.Substring(1), m_HyperVGuidRegExp).Value).ToList();

                var remainingfilters = filters.Where(x => !Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase)).ToArray();
                filter = string.Join(System.IO.Path.PathSeparator.ToString(), remainingfilters);
            }

            if (!Utility.Utility.IsClientWindows)
            {
                Logging.Log.WriteMessage("Hyper-V backup works only on Windows OS.", Logging.LogMessageType.Warning);
               
                if(paths != null)
                    paths = paths.Where(x => !x.Equals(m_HyperVPathAllRegExp, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
            }

            if (paths == null || !ContainFilesForBackup(paths) || !Utility.Utility.IsClientWindows)
                return changedOptions;
            
            if (commandlineOptions.Keys.Contains("vss-exclude-writers"))
            {
                var excludedWriters = commandlineOptions["vss-exclude-writers"].Split(';').Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length > 0).Select(x => new Guid(x)).ToArray();

                if (excludedWriters.Contains(HyperVUtility.HyperVWriterGuid))
                {
                    Logging.Log.WriteMessage(string.Format("Excluded writers for VSS cannot contain Hyper-V writer when backuping Hyper-V virtual machines. Removing \"{0}\" to continue.", HyperVUtility.HyperVWriterGuid.ToString()), Logging.LogMessageType.Warning);

                    changedOptions["vss-exclude-writers"] = string.Join(";", excludedWriters.Where(x => x != HyperVUtility.HyperVWriterGuid));
                }
            }

            if (!commandlineOptions.Keys.Contains("snapshot-policy") || commandlineOptions["snapshot-policy"] != "required")
            {
                Logging.Log.WriteMessage("Snapshot strategy have to be set to \"required\" when backuping Hyper-V virtual machines. Changing to \"required\" to continue.", Logging.LogMessageType.Warning);
                changedOptions["snapshot-policy"] = "required";
            }

            var hypervUtility = new HyperVUtility();

            if (!hypervUtility.IsVSSWriterSupported)
                Logging.Log.WriteMessage("This is client version of Windows. Hyper-V VSS writer is present only on Server version. Backup will continue, but will be crash consistent only in opposite to application consistent in Server version.", Logging.LogMessageType.Warning);

            hypervUtility.QueryHyperVGuestsInfo(true);

            Logging.Log.WriteMessage("Starting to gather Hyper-V information.", Logging.LogMessageType.Information);
            Logging.Log.WriteMessage(string.Format("Found {0} virtual machines on Hyper-V.", hypervUtility.Guests.Count), Logging.LogMessageType.Information);

            List<HyperVGuest> guestsForBackup = new List<HyperVGuest>();

            if (paths.Contains(m_HyperVPathAllRegExp, StringComparer.OrdinalIgnoreCase))
                guestsForBackup = hypervUtility.Guests;
            else
                foreach (var guestID in paths.Where(x => Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray())
                {
                    var foundGuest = hypervUtility.Guests.Where(x => x.ID == new Guid(guestID));

                    if (foundGuest.Count() != 1)
                        throw new Exception(string.Format("Hyper-V guest specified in source with ID {0} cannot be found.", guestID));

                    guestsForBackup.Add(foundGuest.First());
                }

            if (filtersInclude.Count > 0)
                foreach (var guestID in filtersInclude)
                {
                    var foundGuest = hypervUtility.Guests.Where(x => x.ID == new Guid(guestID));

                    if (foundGuest.Count() != 1)
                        throw new Exception(string.Format("Hyper-V guest specified in include filter with ID {0} cannot be found.", guestID));

                    guestsForBackup.Add(foundGuest.First());
                }

            guestsForBackup = guestsForBackup.Distinct().ToList();

            if (filtersExclude.Count > 0)
                foreach (var guestID in filtersExclude)
                {
                    var foundGuest = guestsForBackup.Where(x => x.ID == new Guid(guestID));

                    if (foundGuest.Count() != 1)
                        throw new Exception(string.Format("Hyper-V guest specified in exclude filter with ID {0} cannot be found.", guestID));

                    guestsForBackup.Remove(foundGuest.First());
                }

            var pathsForBackup = new List<string>(paths);

            foreach (var guestForBackup in guestsForBackup)
                foreach (var pathForBackup in guestForBackup.DataPaths)
                {
                    bool bResult;
                    Utility.IFilter matchFilter;
                    var filterhandler = new Utility.FilterExpression(
                        filter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries).Where(x => x.StartsWith("-")).Select(x => x.Substring(1)).ToList());

                    if (!filterhandler.Matches(pathForBackup, out bResult, out matchFilter))
                    {
                        Logging.Log.WriteMessage(string.Format("For VM {0} - adding {1}.", guestForBackup.Name, pathForBackup), Logging.LogMessageType.Information);
                        pathsForBackup.Add(pathForBackup);
                    }
                    else
                        Logging.Log.WriteMessage(string.Format("Excluding {0} based on excluding filters.", pathForBackup), Logging.LogMessageType.Information);
                }

            paths = pathsForBackup.Where(x => !x.Equals(m_HyperVPathAllRegExp, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .Distinct(Utility.Utility.ClientFilenameStringComparer).OrderBy(a => a).ToArray();

            return changedOptions;
        }
        
        public bool ContainFilesForBackup(string[] paths)
        {
            if (paths == null)
                return false;

            return paths.Where(x => x.Equals(m_HyperVPathAllRegExp, StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Count() > 0;
        }

        #endregion
    }
}
