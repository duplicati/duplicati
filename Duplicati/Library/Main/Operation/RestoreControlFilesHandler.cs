// Copyright (C) 2024, The Duplicati Team
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
using System.Text;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    internal class RestoreControlFilesHandler
    {
        private readonly Options m_options;
        private readonly string m_backendurl;
        private readonly RestoreControlFilesResults m_result;

        public RestoreControlFilesHandler(string backendurl, Options options, RestoreControlFilesResults result)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_result = result;
        }

        public void Run(IEnumerable<string> filterstrings = null, Library.Utility.IFilter compositefilter = null)
        {
            if (string.IsNullOrEmpty(m_options.Restorepath))
                throw new Exception("Cannot restore control files without --restore-path");
            if (!System.IO.Directory.Exists(m_options.Restorepath))
                System.IO.Directory.CreateDirectory(m_options.Restorepath);

            using (var tmpdb = new Library.Utility.TempFile())
            using (var db = new Database.LocalDatabase(System.IO.File.Exists(m_options.Dbpath) ? m_options.Dbpath : (string)tmpdb, "RestoreControlFiles", true))
            using (var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            {
                m_result.SetDatabase(db);

                var filter = Library.Utility.JoinedFilterExpression.Join(new Library.Utility.FilterExpression(filterstrings), compositefilter);

                try
                {
                    var filteredList = ListFilesHandler.ParseAndFilterFilesets(backend.List(), m_options);
                    if (filteredList.Count == 0)
                        throw new Exception("No filesets found on remote target");

                    Exception lastEx = new Exception("No suitable files found on remote target");

                    foreach (var fileversion in filteredList)
                        try
                        {
                            if (!m_result.TaskControl.ProgressRendevouz().Await())
                            {
                                backend.WaitForComplete(db, null);
                                return;
                            }

                            var file = fileversion.Value.File;
                            var entry = db.GetRemoteVolume(file.Name);

                            var res = new List<string>();
                            using (var tmpfile = backend.Get(file.Name, entry.Size < 0 ? file.Size : entry.Size, entry.Hash))
                            using (var tmp = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(file.Name), tmpfile, m_options))
                                foreach (var cf in tmp.ControlFiles)
                                    if (Library.Utility.FilterExpression.Matches(filter, cf.Key))
                                    {
                                        var targetpath = System.IO.Path.Combine(m_options.Restorepath, cf.Key);
                                        using (var ts = System.IO.File.Create(targetpath))
                                            Library.Utility.Utility.CopyStream(cf.Value, ts);
                                        res.Add(targetpath);
                                    }

                            m_result.SetResult(res);

                            lastEx = null;
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                        }

                    if (lastEx != null)
                        throw lastEx;
                }
                finally
                {
                    backend.WaitForComplete(db, null);
                }

                db.WriteResults();
            }
        }
    }
}
