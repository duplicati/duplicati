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
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class AmzCD 
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to Amazon Cloud Drive. Supported format is ""amzcd://folder/subfolder""."); } }
        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }
        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }
        public static string DisplayName { get { return LC.L(@"Amazon Cloud Drive"); } }
        public static string MissingAuthID(string url) { return LC.L(@"You need an AuthID, you can get it from: {0}"); }
        public static string LabelsShort { get { return LC.L(@"The labels to set"); } }
        public static string LabelsLong { get { return LC.L(@"Use this option to set labels on the files and folders created"); } }
        public static string MultipleEntries(string folder, string parent) { return LC.L(@"There is more than one item named ""{0}"" in the folder ""{1}"""); }
        public static string DelayShort { get { return LC.L(@"The consistency delay"); } }
        public static string DelayLong { get { return LC.L(@"Amazon Cloud drive needs a small delay for results to stay consistent."); } }
            }
}

