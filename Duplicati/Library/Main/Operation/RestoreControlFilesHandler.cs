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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    internal class RestoreControlFilesHandler
    {
        private readonly Options m_options;
        private readonly RestoreControlFilesResults m_result;

        public RestoreControlFilesHandler(Options options, RestoreControlFilesResults result)
        {
            m_options = options;
            m_result = result;
        }

        public async Task RunAsync(IEnumerable<string> filterstrings, IBackendManager backendManager, Library.Utility.IFilter compositefilter)
        {
            if (string.IsNullOrEmpty(m_options.Restorepath))
                throw new Exception("Cannot restore control files without --restore-path");
            if (!Directory.Exists(m_options.Restorepath))
                Directory.CreateDirectory(m_options.Restorepath);

            using var tmpdb = new TempFile();
            await using var db = await Database.LocalDatabase.CreateLocalDatabaseAsync(File.Exists(m_options.Dbpath) ? m_options.Dbpath : (string)tmpdb, "RestoreControlFiles", true, m_options.SqlitePageCache).ConfigureAwait(false);
            var filter = JoinedFilterExpression.Join(new FilterExpression(filterstrings), compositefilter);

            try
            {
                var filteredList = ListFilesHandler.ParseAndFilterFilesets(await backendManager.ListAsync(m_result.TaskControl.ProgressToken).ConfigureAwait(false), m_options);
                if (filteredList.Count == 0)
                    throw new Exception("No filesets found on remote target");

                var lastEx = new Exception("No suitable files found on remote target");

                foreach (var fileversion in filteredList)
                    try
                    {
                        if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        {
                            await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                            return;
                        }

                        var file = fileversion.Value.File;
                        var entry = await db
                            .GetRemoteVolume(file.Name)
                            .ConfigureAwait(false);

                        var res = new List<string>();
                        using (var tmpfile = await backendManager.GetAsync(file.Name, entry.Hash, entry.Size < 0 ? file.Size : entry.Size, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                        using (var tmp = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(file.Name), tmpfile, m_options))
                            foreach (var cf in tmp.ControlFiles)
                                if (FilterExpression.Matches(filter, cf.Key))
                                {
                                    var targetpath = Path.Combine(m_options.Restorepath, cf.Key);
                                    using (var ts = File.Create(targetpath))
                                        await Library.Utility.Utility.CopyStreamAsync(cf.Value, ts, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                    res.Add(targetpath);
                                }

                        m_result.SetResult(res);

                        lastEx = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        if (ex.IsAbortException())
                            throw;
                    }

                if (lastEx != null)
                    throw lastEx;
            }
            finally
            {
                await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            }
        }
    }
}
