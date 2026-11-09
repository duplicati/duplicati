using System;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending hard-link operation: a second remote name for the content
    /// of an existing remote file. Only queued for backends that implement both
    /// <see cref="ILinkEnabledBackend"/> and <see cref="IFolderEnabledBackend"/>, so both
    /// names are full relative paths against the base backend URL.
    /// </summary>
    /// <param name="existingName">The relative path of the existing remote file</param>
    /// <param name="targetName">The relative path of the link to create</param>
    /// <param name="context">The execution context</param>
    /// <param name="cancelToken">The cancellation token</param>
    private class CreateHardLinkOperation(string existingName, string targetName, ExecuteContext context, CancellationToken cancelToken)
        : PendingOperation<bool>(context, true, cancelToken)
    {
        /// <summary>
        /// The relative path of the existing remote file
        /// </summary>
        public string ExistingName { get; } = existingName;

        /// <summary>
        /// The relative path of the link to create
        /// </summary>
        public override string RemoteFilename { get; } = targetName;

        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.CreateHardLink;

        /// <summary>
        /// Executes the operation
        /// </summary>
        /// <param name="backend">The backend to create the link on</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>An awaitable task returning true when the link was created</returns>
        public override async Task<bool> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            if (backend is not ILinkEnabledBackend linkBackend)
                throw new HardLinkNotSupportedException();

            Context.Statwriter.SendEvent(BackendActionType.CreateHardLink, BackendEventType.Started, RemoteFilename, 0);
            await linkBackend.CreateHardLinkAsync(ExistingName, RemoteFilename, cancelToken).ConfigureAwait(false);
            Context.Statwriter.SendEvent(BackendActionType.CreateHardLink, BackendEventType.Completed, RemoteFilename, 0);
            return true;
        }
    }
}
