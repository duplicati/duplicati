#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
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
using System.Collections.Generic;

namespace Duplicati.Library.Main.Volumes
{
    public class SubFolderFilePlacementUtils
    {
        private static readonly object _additionalVolumeFileCountLock = new object();
        private static long _additionalVolumeFileCount;

        public static long VolumeFileCount
        {
            get => _additionalVolumeFileCount;
            set
            {
                lock (_additionalVolumeFileCountLock)
                {
                    _additionalVolumeFileCount = value;
                }
            }
        }

        private static List<string> GetFolderPathsForLevel(long numSubFolders, int maxReturn, string path = "")
        {
            List<string> newItems = new List<string>();
            long max = maxReturn < numSubFolders ? maxReturn : numSubFolders;

            for (long j = 1; j <= max; j++)
            {
                newItems.Add(string.IsNullOrEmpty(path) ? $"{j}/" : $"{path}{j}/");
            }
            return newItems;
        }

        public static string GetFileFolderPathPlacementUsingFlatStructure(long volumeCount, long maxFilesPerFolder, long numSubFoldersPerFolder)
        {
            if (maxFilesPerFolder == 0 || numSubFoldersPerFolder == 0)
            {
                return string.Empty;
            }

            long combinedFileCount = volumeCount + _additionalVolumeFileCount;
            int step = (int)(combinedFileCount / maxFilesPerFolder);
            List<string> items = GetFolderPathsForLevel(numSubFoldersPerFolder, step);
            List<string> allPaths = new List<string> { "" };
            allPaths.AddRange(items);
            List<string> moreItems = new List<string>(items);
            List<string> newItems = new List<string>();
            int stepPointer = allPaths.Count;

            if (step <= numSubFoldersPerFolder)
            {
                return allPaths[step];
            }

            while (true)
            {
                newItems.Clear();
                foreach (var item in moreItems)
                {
                    for (var j = 1; j <= numSubFoldersPerFolder; j++)
                    {
                        newItems.Add(string.IsNullOrEmpty(item) ? $"{j}/" : $"{item}{j}/");
                        if (stepPointer++ != step) continue;
                        return string.IsNullOrEmpty(item) ? $"{j}/" : $"{item}{j}/";
                    }
                }

                allPaths.AddRange(newItems);
                moreItems = new List<string>(newItems);
            }
        }
    }

}
