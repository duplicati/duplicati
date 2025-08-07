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
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation
{
    internal class VacuumHandler
    {
        private readonly Options m_options;
        private readonly VacuumResults m_result;

        public VacuumHandler(Options options, VacuumResults result)
        {
            m_options = options;
            m_result = result;
        }

        public virtual async Task RunAsync()
        {
            await using var db =
                await Database.LocalDatabase.CreateLocalDatabaseAsync(m_options.Dbpath, "Vacuum", false, null, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Vacuum_Running);
            await db.Transaction.CommitAsync("Vacuum", false, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            await db.Vacuum(m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            m_result.EndTime = DateTime.UtcNow;
        }
    }
}
