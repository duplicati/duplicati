using System.Collections.Generic;
using System.Threading;

namespace Duplicati.Library.Main.Volumes
{
    public class SubFolderFilePlacementUtils
    {
        private static readonly SemaphoreSlim VolumeFileCountUpdateSemaphore = new SemaphoreSlim(1, 1);
        private static readonly object _additionalVolumeFileCountLock = new object();
        private static long _additionalVolumeFileCount;

        public static long VolumeFileCount
        {
            get => _additionalVolumeFileCount;
            set
            {
                //VolumeFileCountUpdateSemaphore.Wait();
                lock (_additionalVolumeFileCountLock)
                {
                    _additionalVolumeFileCount = value;
                }
                //VolumeFileCountUpdateSemaphore.Release();
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
