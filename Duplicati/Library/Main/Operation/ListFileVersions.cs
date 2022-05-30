using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using System.Linq;

namespace Duplicati.Library.Main.Operation
{
    internal class ListFileVersions
    {
        private readonly Options m_options;
        private readonly ListResultFileVersions m_result;

        public ListFileVersions(Options options, ListResultFileVersions result)
        {
            m_options = options;
            m_result = result;
        }

        public void Run(string file)
        {
            if (!m_options.NoLocalDb && System.IO.File.Exists(m_options.Dbpath))
            {
                using (var db = new LocalListDatabase(m_options.Dbpath))
                {
                    var filesets = db.GetFilesetsForFile(file);
                    m_result.SetResult(filesets.Select(v => new ListResultFileVersion(v.FileId, v.FileSize, v.LastModified, v.Timestamp)).ToArray());
                }
            }
            else
            {
                throw new UserInformationException("Restoring a specific version of a file is only supported with a local database. Consider using the \"repair\" option to rebuild the database.", "FileVersionRestoreRequiresLocalDatabase");
            }
        }
    }
}
