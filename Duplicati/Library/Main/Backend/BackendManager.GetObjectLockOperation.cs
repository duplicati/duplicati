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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending get object lock operation
    /// </summary>
    private class GetObjectLockOperation : PendingOperation<DateTime?>
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<GetObjectLockOperation>();

        /// <summary>
        /// The remote filename to check for lock
        /// </summary>
        public override string RemoteFilename { get; }

        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.GetObjectLock;

        /// <summary>
        /// Creates a new GetObjectLockOperation
        /// </summary>
        /// <param name="remoteName">The remote file name to check</param>
        /// <param name="context">The execution context</param>
        /// <param name="cancelToken">The cancellation token</param>
        public GetObjectLockOperation(string remoteName, ExecuteContext context, CancellationToken cancelToken)
            : base(context, true, cancelToken)
        {
            RemoteFilename = remoteName;
        }

        /// <inheritdoc />
        public override async Task<DateTime?> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            if (backend is not ILockingBackend lockingBackend)
                throw new NotSupportedException("Backend does not support object locking operations");

            Context.Statwriter.SendEvent(BackendActionType.GetObjectLock, BackendEventType.Started, RemoteFilename, 0);

            try
            {
                return await lockingBackend.GetObjectLockUntilAsync(RemoteFilename, cancelToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "GetObjectLockFailed", ex, "Failed to get object lock for {0}: {1}", RemoteFilename, ex.Message);
                throw;
            }
            finally
            {
                Context.Statwriter.SendEvent(BackendActionType.GetObjectLock, BackendEventType.Completed, RemoteFilename, 0);
            }
        }
    }
}
