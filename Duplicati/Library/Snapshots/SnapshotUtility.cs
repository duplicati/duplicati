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
        /// <returns>The ISnapshotService implementation</returns>
        public static ISnapshotService CreateSnapshot(string[] folders)
        {
            if (Core.Utility.IsClientLinux)
                return new LinuxSnapshot(folders);
            else
                return new WindowsSnapshot(folders);
        }
    }
}
