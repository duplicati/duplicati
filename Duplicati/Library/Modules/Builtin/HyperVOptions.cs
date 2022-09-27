﻿#region Disclaimer / License
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
using Duplicati.Library.Common;
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.Modules.Builtin
{
    public class HyperVOptions : Interface.IGenericSourceModule
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<HyperVOptions>();
        private const string m_HyperVPathGuidRegExp = @"\%HYPERV\%\\([0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12})";
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
            get { return Platform.IsClientWindows; }
        }

        public IList<Interface.ICommandLineArgument> SupportedCommands
        {
            get { return null; }
        }

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            // Do nothing.  Implementation needed for IGenericModule interface.
        }

        #endregion

        #region Implementation of IGenericSourceModule
        public Dictionary<string, string> ParseSourcePaths(ref string[] paths, ref string filter, Dictionary<string, string> commandlineOptions)
        {
            // Early exit in case we are non-windows to prevent attempting to load Windows-only components
            if (!Platform.IsClientWindows)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "HyperVWindowsOnly", null, "Hyper-V backup works only on Windows OS");

                if (paths != null)
                    paths = paths.Where(x => !x.Equals(m_HyperVPathAllRegExp, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();

                if (!string.IsNullOrEmpty(filter))
                {
                    var filters = filter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);
                    var remainingfilters = filters.Where(x => !Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
                    filter = string.Join(System.IO.Path.PathSeparator.ToString(), remainingfilters);
                }

                return new Dictionary<string, string>();
            }

            // Windows, do the real stuff!
            return RealParseSourcePaths(ref paths, ref filter, commandlineOptions);
        }

        // Make sure the JIT does not attempt to inline this call and thus load
        // referenced types from System.Management here
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private Dictionary<string, string> RealParseSourcePaths(ref string[] paths, ref string filter, Dictionary<string, string> commandlineOptions)
        {
            var changedOptions = new Dictionary<string, string>();
            var filtersInclude = new List<string>();
            var filtersExclude = new List<string>();

            if (!string.IsNullOrEmpty(filter))
            {
                var filters = filter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);

                filtersInclude = filters.Where(x => x.StartsWith("+", StringComparison.Ordinal) && Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    .Select(x => Regex.Match(x.Substring(1), m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups[1].Value).ToList();
                filtersExclude = filters.Where(x => x.StartsWith("-", StringComparison.Ordinal) && Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    .Select(x => Regex.Match(x.Substring(1), m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups[1].Value).ToList();

                var remainingfilters = filters.Where(x => !Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
                filter = string.Join(System.IO.Path.PathSeparator.ToString(), remainingfilters);
            }

            var hypervUtility = new HyperVUtility();

            if (paths == null || !ContainFilesForBackup(paths) || !hypervUtility.IsHyperVInstalled)
                return changedOptions;

            if (!hypervUtility.IsVSSWriterSupported)
                Logging.Log.WriteWarningMessage(LOGTAG, "HyperVOnServerOnly", null, "This is client version of Windows. Hyper-V VSS writer is present only on Server version. Backup will continue, but will be crash consistent only in opposite to application consistent in Server version");

            if (commandlineOptions.Keys.Contains("vss-exclude-writers"))
            {
                var excludedWriters = commandlineOptions["vss-exclude-writers"].Split(';').Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length > 0).Select(x => new Guid(x)).ToArray();

                if (excludedWriters.Contains(HyperVUtility.HyperVWriterGuid) && hypervUtility.IsVSSWriterSupported)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "CannotExcludeHyperVVSSWriter", null, "Excluding Hyper-V writer for VSS on Server OS. Backup will continue, but will be crash consistent only in opposite to application consistent");
                    changedOptions["vss-exclude-writers"] = string.Join(";", excludedWriters.Where(x => x != HyperVUtility.HyperVWriterGuid));
                }
            }
            
            if (!commandlineOptions.Keys.Contains("snapshot-policy") || !commandlineOptions["snapshot-policy"].Equals("required", StringComparison.OrdinalIgnoreCase))
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "MustSetSnapshotPolicy", null, "Snapshot policy have to be set to \"required\" when backuping Hyper-V virtual machines. Changing to \"required\" to continue", Logging.LogMessageType.Warning);
                changedOptions["snapshot-policy"] = "required";
            }
            
            Logging.Log.WriteInformationMessage(LOGTAG, "StartingHyperVQuery", "Starting to gather Hyper-V information");
            hypervUtility.QueryHyperVGuestsInfo(true);          
            Logging.Log.WriteInformationMessage(LOGTAG, "HyperVMachineCount", "Found {0} virtual machines on Hyper-V", hypervUtility.Guests.Count);

            foreach (var guest in hypervUtility.Guests)
                Logging.Log.WriteProfilingMessage(LOGTAG, "FoundHyperVMachine", "Found VM name {0}, ID {1}, files {2}", guest.Name, guest.ID, string.Join(";", guest.DataPaths));

            List<HyperVGuest> guestsForBackup = new List<HyperVGuest>();

            if (paths.Contains(m_HyperVPathAllRegExp, StringComparer.OrdinalIgnoreCase))
                guestsForBackup = hypervUtility.Guests;
            else
                foreach (var guestID in paths.Where(x => Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    .Select(x => Regex.Match(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups[1].Value).ToArray())
                {
                    var foundGuest = hypervUtility.Guests.Where(x => x.ID == new Guid(guestID));

                    if (foundGuest.Count() != 1)
                        throw new Duplicati.Library.Interface.UserInformationException(string.Format("Hyper-V guest specified in source with ID {0} cannot be found", guestID), "HyperVGuestNotFound");

                    guestsForBackup.Add(foundGuest.First());
                }

            if (filtersInclude.Count > 0)
                foreach (var guestID in filtersInclude)
                {
                    var foundGuest = hypervUtility.Guests.Where(x => x.ID == new Guid(guestID));

                    if (foundGuest.Count() != 1)
                        throw new Duplicati.Library.Interface.UserInformationException(string.Format("Hyper-V guest specified in include filter with ID {0} cannot be found", guestID), "HyperVGuestNotFound");

                    guestsForBackup.Add(foundGuest.First());
                    Logging.Log.WriteInformationMessage(LOGTAG, "IncludeByFilter", "Including {0} based on including filters", guestID);
                }

            guestsForBackup = guestsForBackup.Distinct().ToList();

            if (filtersExclude.Count > 0)
                foreach (var guestID in filtersExclude)
                {
                    var foundGuest = guestsForBackup.Where(x => x.ID == new Guid(guestID));

                    if (foundGuest.Count() != 1)
                        throw new Duplicati.Library.Interface.UserInformationException(string.Format("Hyper-V guest specified in exclude filter with ID {0} cannot be found", guestID), "HyperVGuestNotFound");

                    guestsForBackup.Remove(foundGuest.First());
                    Logging.Log.WriteInformationMessage(LOGTAG, "ExcludeByFilter", "Excluding {0} based on excluding filters", guestID);
                }

            var pathsForBackup = new List<string>(paths);
            var filterhandler = new Utility.FilterExpression(
                filter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries).Where(x => x.StartsWith("-", StringComparison.Ordinal)).Select(x => x.Substring(1)).ToList());
            
            foreach (var guestForBackup in guestsForBackup)
                foreach (var pathForBackup in guestForBackup.DataPaths)
                {
                    if (!filterhandler.Matches(pathForBackup, out _, out _))
                    {
                        Logging.Log.WriteInformationMessage(LOGTAG, "IncludeHyperV", "For VM {0} - adding {1}", guestForBackup.Name, pathForBackup);
                        pathsForBackup.Add(pathForBackup);
                    }
                    else
                        Logging.Log.WriteInformationMessage(LOGTAG, "ExcludeByFilter", "Excluding {0} based on excluding filters", pathForBackup);
                }

            paths = pathsForBackup.Where(x => !x.Equals(m_HyperVPathAllRegExp, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .Distinct(Utility.Utility.ClientFilenameStringComparer).OrderBy(a => a).ToArray();

            return changedOptions;
        }
        
        public bool ContainFilesForBackup(string[] paths)
        {
            if (paths == null || !Platform.IsClientWindows)
                return false;

            return paths.Where(x => !string.IsNullOrWhiteSpace(x)).Any(x => x.Equals(m_HyperVPathAllRegExp, StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(x, m_HyperVPathGuidRegExp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        #endregion
    }
}
