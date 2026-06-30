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
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Interface for implementing restore-specific callback modules.
    /// 
    /// Modules implementing this interface are notified at key points during a
    /// restore operation: when the priority-files list is being prepared (allowing
    /// the module to modify it), when the bulk restore starts (after all priority
    /// files have been restored), and for each individual file that is restored.
    /// 
    /// The per-file callback (<see cref="OnFileRestoredAsync"/>) is only invoked when
    /// the new restore engine is used (i.e. when <c>--restore-legacy</c> is not
    /// set). The <see cref="OnPreparePriorityFilesAsync"/> and
    /// <see cref="OnBulkRestoreStartAsync"/> callbacks are invoked for both the legacy
    /// and the new restore engines.
    /// </summary>
    public interface IRestoreCallbackModule : IGenericModule
    {
        /// <summary>
        /// Called when the priority-files list has been collected from the restore
        /// destination, but before any files are restored. The module is free to
        /// inspect and modify the list in place (add, remove or reorder entries)
        /// to influence which files are restored first.
        /// </summary>
        /// <param name="priorityFiles">The list of priority file names. The module may modify this list.</param>
        /// <param name="version">The 0-based backup version index being restored (0 = newest).</param>
        /// <param name="backupTimestamp">The timestamp of the backup version being restored, in UTC.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task OnPreparePriorityFilesAsync(IList<string> priorityFiles, long version, DateTime backupTimestamp, CancellationToken cancellationToken);

        /// <summary>
        /// Called once all priority files have been restored and the bulk restore
        /// of the remaining files is about to start. If there are no priority
        /// files, this is called before the single bulk restore pass begins.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task OnBulkRestoreStartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Called for each restore target that has been restored, with the backup version
        /// index (0 being the newest backup) the target was restored from, the
        /// timestamp of that backup version, and the target path. The callback fires for
        /// regular files as well as folders (whose metadata is restored) and alternate data
        /// streams; implementations that only care about regular files should filter by
        /// path themselves. This is only invoked when the new restore engine is used (i.e.
        /// <c>--restore-legacy</c> is not set).
        /// </summary>
        /// <param name="version">The 0-based backup version index the target was restored from (0 = newest).</param>
        /// <param name="path">The target path of the restored file, folder or alternate data stream.</param>
        /// <param name="backupTimestamp">The timestamp of the backup version the target was restored from, in UTC.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task OnFileRestoredAsync(long version, string path, DateTime backupTimestamp, CancellationToken cancellationToken);
    }
}
