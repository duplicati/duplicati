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
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.Modules.Builtin
{
    public class HyperVOptions : Interface.IGenericModule, Interface.IGenericSourceModule
    {
        private const string OPTION_SOURCE = "hyperv-backup-vm";

        #region IGenericModule Members

        public string Key
        {
            get { return "hyperv-options"; }
        }

        public string DisplayName
        {
            get { return Strings.HttpOptions.DisplayName; }
        }

        public string Description
        {
            get { return Strings.HttpOptions.Description; }
        }

        public bool LoadAsDefault
        {
            get { return true; }
        }

        public IList<Duplicati.Library.Interface.ICommandLineArgument> SupportedCommands
        {
            get { return new List<Duplicati.Library.Interface.ICommandLineArgument>(); }
        }

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
        }

        #endregion
        
        #region Implementation of IGenericSourceModule
        public void ParseSource(ref string[] paths, ref Dictionary<string, string> commandlineOptions)
        {
            var hypervpathguidexp = @"%HYPERV:(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}%";
            var hypervguidexp = @"(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}";
            var hypervpathallexp = @"%HYPERV:*%";
            var pathshyperv = new List<string>();

            if (paths.Contains(hypervpathallexp, StringComparer.OrdinalIgnoreCase))
                pathshyperv = new HyperVUtility().GetHyperVGuests().Select(x => string.Format("%HYPERV:{0}%", x.ID)).ToList();
            else
                pathshyperv = paths.Where(x => Regex.IsMatch(x, hypervpathguidexp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToList();

            paths = paths.Where(x => !x.Equals(hypervpathallexp, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(x, hypervpathguidexp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
            
            if (commandlineOptions.Keys.Contains("filter"))
            {
                var filters = commandlineOptions["filter"].Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);
                var filtersInclude = filters.Where(x => x.StartsWith("+") && Regex.IsMatch(x, hypervpathguidexp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Select(x => x.Substring(1)).ToList();
                var filtersExclude = filters.Where(x => x.StartsWith("-") && Regex.IsMatch(x, hypervpathguidexp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Select(x => x.Substring(1)).ToList();

                var remainingfilters = filters.Where(x => !Regex.IsMatch(x, hypervpathguidexp, RegexOptions.IgnoreCase)).ToArray();
                commandlineOptions["filter"] = string.Join(System.IO.Path.PathSeparator.ToString(), remainingfilters);

                pathshyperv = pathshyperv.Union(filtersInclude).Except(filtersExclude).ToList();
            }

            pathshyperv = pathshyperv.Select(x => Regex.Match(x, hypervguidexp).Value).ToList();
            commandlineOptions[OPTION_SOURCE] = string.Join(System.IO.Path.PathSeparator.ToString(), pathshyperv);
        }

        #endregion
    }
}
