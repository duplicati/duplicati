// Copyright (C) 2025, The Duplicati Team
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
using System.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation
{
    internal class CreateBugReportHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<CreateBugReportHandler>();
        private string m_targetpath;
        private readonly Options m_options;
        private readonly CreateLogDatabaseResults m_result;

        public CreateBugReportHandler(string targetpath, Options options, CreateLogDatabaseResults result)
        {
            m_targetpath = targetpath;
            m_options = options;
            m_result = result;
        }

        public void Run()
        {
            var ext = System.IO.Path.GetExtension(m_targetpath);
            var module = m_options.CompressionModule;

            if (ext == "" || string.Compare(ext, 1, module, 0, module.Length, StringComparison.OrdinalIgnoreCase) != 0)
                m_targetpath = m_targetpath + "." + module;

            if (System.IO.File.Exists(m_targetpath))
                throw new UserInformationException(string.Format("Output file already exists, not overwriting: {0}", m_targetpath), "BugReportTargetAlreadyExists");

            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "BugReportSourceDatabaseNotFound");

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.BugReport_Running);
            m_result.OperationProgressUpdater.UpdateProgress(0);

            Logging.Log.WriteInformationMessage(LOGTAG, "ScrubbingFilenames", "Scrubbing filenames from database, this may take a while, please wait");

            using (var tmp = new Library.Utility.TempFile())
            {
                System.IO.File.Copy(m_options.Dbpath, tmp, true);
                using (var db = new LocalBugReportDatabase(tmp))
                {
                    m_result.SetDatabase(db);
                    db.Fix();
                    if (m_options.AutoVacuum)
                    {
                        db.Vacuum();
                    }
                }

                using (var stream = new System.IO.FileStream(m_targetpath, FileMode.Create, FileAccess.Write, FileShare.Read))
                using (ICompression cm = DynamicLoader.CompressionLoader.GetModule(module, stream, Interface.ArchiveMode.Write, m_options.RawOptions))
                {
                    using (var cs = cm.CreateFile("log-database.sqlite", Duplicati.Library.Interface.CompressionHint.Compressible, DateTime.UtcNow))
                    using (var fs = System.IO.File.Open(tmp, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                        Library.Utility.Utility.CopyStream(fs, cs);

                    using (var cs = new System.IO.StreamWriter(cm.CreateFile("system-info.txt", Duplicati.Library.Interface.CompressionHint.Compressible, DateTime.UtcNow)))
                        foreach (var line in SystemInfoHandler.GetSystemInfo())
                            cs.WriteLine(line);
                }

                m_result.TargetPath = m_targetpath;
            }
        }
    }
}

