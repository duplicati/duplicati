// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;

namespace Duplicati.Library.Snapshots.Windows
{
    /// <summary>
    /// An MSSQL instance
    /// </summary>
    public sealed record MSSQLDB
    {
        /// <summary>
        /// The database name
        /// </summary>
        public required string Database { get; init; }
        /// <summary>
        /// The server name
        /// </summary>
        public required string Server { get; init; }
        /// <summary>
        /// The instance id (empty for default instance)
        /// </summary>
        public required string InstanceId { get; init; }
        /// <summary>
        /// The paths related to the database
        /// </summary>
        public required List<string> DataPaths { get; init; }

        /// <summary>
        /// The server instance id, e.g. "Server\Instance" or just "Server"
        /// </summary>
        public string ServerInstanceId => string.IsNullOrEmpty(InstanceId) ? Server : $"{Server}\\{InstanceId}";
    }

    public interface IMSSQLUtility
    {
        /// <summary>
        /// The MS SQL VSS Writer Guid
        /// </summary>
        Guid MSSQLWriterGuid { get; }
        /// <summary>
        /// MS SQL is supported only on Windows platform
        /// </summary>
        bool IsMSSQLInstalled { get; }
        /// <summary>
        /// Enumerated MS SQL DBs
        /// </summary>
        List<MSSQLDB> DBs { get; }

        /// <summary>
        /// For all MS SQL databases it enumerate all associated paths using VSS data
        /// </summary>
        /// <returns>A collection of DBs and paths</returns>
        void QueryDBsInfo(WindowsSnapshotProvider provider);
    }

    [SupportedOSPlatform("windows")]
    public class MSSQLUtility : IMSSQLUtility
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<MSSQLUtility>();
        /// <summary>
        /// The MS SQL VSS Writer Guid
        /// </summary>
        private static readonly Guid _MSSQLWriterGuid = new Guid("a65faa63-5ea8-4ebc-9dbd-a0c4db26912a");
        /// <summary>
        /// The MS SQL VSS Writer Guid
        /// </summary>
        public Guid MSSQLWriterGuid => _MSSQLWriterGuid;
        /// <summary>
        /// MS SQL is supported only on Windows platform
        /// </summary>
        public bool IsMSSQLInstalled { get; }
        /// <summary>
        /// Enumerated MS SQL DBs
        /// </summary>
        public List<MSSQLDB> DBs { get { return m_DBs; } }
        private readonly List<MSSQLDB> m_DBs = new();

        public MSSQLUtility()
        {
            if (!OperatingSystem.IsWindows())
            {
                IsMSSQLInstalled = false;
                return;
            }

            string[] arrInstalledInstances = null;

            var installed = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SQL Server", "InstalledInstances", "");
            if (installed is string s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    arrInstalledInstances = new string[] { s };
            }
            else if (installed is string[] strings)
                arrInstalledInstances = strings;
            else if (installed != null)
                try { arrInstalledInstances = (string[])installed; }
                catch { }

            if (Environment.Is64BitOperatingSystem && arrInstalledInstances == null)
            {
                var installed32on64 = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Microsoft SQL Server", "InstalledInstances", "");
                if (installed32on64 is string on64)
                {
                    if (!string.IsNullOrWhiteSpace(on64))
                        arrInstalledInstances = new string[] { on64 };
                }
                else if (installed32on64 is string[] strings)
                    arrInstalledInstances = strings;
                else if (installed32on64 != null)
                    try { arrInstalledInstances = (string[])installed32on64; }
                    catch { }
            }

            IsMSSQLInstalled = arrInstalledInstances != null && arrInstalledInstances.Length > 0;

            if (!IsMSSQLInstalled)
                Logging.Log.WriteInformationMessage(LOGTAG, "NoMSSQLInstance", "Cannot find any MS SQL Server instance. MS SQL Server is probably not installed.");
        }

        /// <summary>
        /// For all MS SQL databases it enumerate all associated paths using VSS data
        /// </summary>
        /// <returns>A collection of DBs and paths</returns>
        public void QueryDBsInfo(WindowsSnapshotProvider provider)
        {
            if (!IsMSSQLInstalled)
                return;

            m_DBs.Clear();

            using (var vssBackupComponents = new SnapshotManager(provider))
            {
                var writerGUIDS = new[] { _MSSQLWriterGuid };
                try
                {
                    vssBackupComponents.SetupWriters(writerGUIDS, null);
                }
                catch (Exception)
                {
                    throw new Interface.UserInformationException("Microsoft SQL Server VSS Writer not found - cannot backup SQL databases.", "NoMsSqlVssWriter");
                }

                foreach (var o in vssBackupComponents.ParseWriterMetaData(writerGUIDS))
                {
                    if (string.IsNullOrWhiteSpace(o.LogicalPath))
                        continue;

                    // The logical path is in the format "SERVERNAME\INSTANCENAME" or "SERVERNAME"
                    // We need to split it into the server name and the instance name (if any)
                    var pathcomp = o.LogicalPath.Split(['\\'], 2, StringSplitOptions.RemoveEmptyEntries);
                    m_DBs.Add(new MSSQLDB()
                    {
                        Database = o.Name,
                        Server = pathcomp[0],
                        InstanceId = pathcomp.Length > 1 ? pathcomp[1] : string.Empty,
                        DataPaths = o.Paths
                            .ConvertAll(m => m[0].ToString().ToUpperInvariant() + m.Substring(1))
                            .Distinct(Utility.Utility.ClientFilenameStringComparer)
                            .OrderBy(a => a).ToList()
                    });
                }
            }
        }
    }
}
