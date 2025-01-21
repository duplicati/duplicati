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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{
    internal class ListAffected
    {
        private readonly Options m_options;
        private readonly ListAffectedResults m_result;

        public ListAffected(Options options, ListAffectedResults result)
        {
            m_options = options;
            m_result = result;
        }
            
        public void Run(List<string> args, Action<Duplicati.Library.Interface.IListAffectedResults> callback = null)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            using(var db = new Database.LocalListAffectedDatabase(m_options.Dbpath))
            {
                m_result.SetDatabase(db);
                if (callback == null)
                {
                    m_result.SetResult(
                        db.GetFilesets(args).OrderByDescending(x => x.Time).ToArray(),
                        db.GetFiles(args).ToArray(),
                        db.GetLogLines(args).ToArray(),
                        db.GetVolumes(args).ToArray()
                    );
                }
                else
                {
                    m_result.SetResult(
                        db.GetFilesets(args).OrderByDescending(x => x.Time),
                        db.GetFiles(args),
                        db.GetLogLines(args),
                        db.GetVolumes(args)
                    );

                    callback(m_result);
                }
            }
        }
    }
}

