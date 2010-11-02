using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Simple helper to initialize and load a snapshot implementation for the current OS
    /// </summary>
    public static class SnapshotUtility
    {
        /// <summary>
        /// Loads a snapshot implementation for the current OS
        /// </summary>
        /// <param name="folders">The list of folders to create snapshots of</param>
        /// <param name="options">A set of commandline options</param>
        /// <returns>The ISnapshotService implementation</returns>
        public static ISnapshotService CreateSnapshot(string[] folders, Dictionary<string, string> options)
        {
            if (Core.Utility.IsClientLinux)
                return new LinuxSnapshot(folders, options);
            else
                return new WindowsSnapshot(folders, options);
        }
    }
}
