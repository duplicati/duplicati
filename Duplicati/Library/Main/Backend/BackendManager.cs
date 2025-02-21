using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Backend;

#nullable enable

/// <summary>
/// The backend manager
/// </summary>
internal partial class BackendManager : IBackendManager
{
    /// <summary>
    /// The log tag for the class
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<BackendManager>();

    /// <summary>
    /// The channel for issuing and handling requests
    /// </summary>
    private readonly IChannel<PendingOperationBase> requestChannel = ChannelManager.CreateChannel<PendingOperationBase>(name: "BackendManager");

    /// <summary>
    /// The queue runner task
    /// </summary>
    private readonly Task queueRunner;

    /// <summary>
    /// The last file read size
    /// </summary>
    public long LastReadSize { get; private set; }
    /// <summary>
    /// The last file write size
    /// </summary>
    public long LastWriteSize { get; private set; }

    /// <summary>
    /// The execution context
    /// </summary>
    private readonly ExecuteContext context;

    /// <summary>
    /// Flag keeping track of whether the object has been disposed
    /// </summary>
    private bool isDisposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackendManager"/> class.
    /// </summary>
    /// <param name="backendUrl">The backend URL</param>
    /// <param name="options">The options</param>
    /// <param name="backendWriter">The backend writer</param>
    /// <param name="taskReader">The task reader</param>
    public BackendManager(string backendUrl, Options options, IBackendWriter backendWriter, ITaskReader taskReader)
    {
        if (string.IsNullOrWhiteSpace(backendUrl))
            throw new ArgumentNullException(nameof(backendUrl));

        // To avoid excessive parameter passing, the context is captured here
        context = new ExecuteContext(
            new ProgressHandler(options, backendWriter, taskReader).HandleProgress,
            backendWriter ?? throw new ArgumentNullException(nameof(backendWriter)),
            new DatabaseCollector(),
            taskReader ?? throw new ArgumentNullException(nameof(taskReader)),
            options ?? throw new ArgumentNullException(nameof(options))
        );

        // The BackendManager class is a wrapper that essentially sends
        // requests into a queue and processes them in order.
        // The Handler class is the one that actually processes the requests.
        queueRunner = Handler.RunHandlerAsync(
            requestChannel,
            backendUrl,
            context);
    }

    /// <summary>
    /// Enters a task into the queue for processing.
    /// </summary>
    /// <param name="op">The operation to queue</param>
    /// <returns>An awaitable task</returns>
    private async Task QueueTask(PendingOperationBase op)
    {
        if (queueRunner.IsCompleted)
        {
            if (queueRunner.IsFaulted)
                await queueRunner.ConfigureAwait(false);
            if (queueRunner.IsCanceled)
                throw new OperationCanceledException("Backend manager is stopped", queueRunner.Exception);
            throw new InvalidOperationException("Backend manager is stopped");
        }

        try
        {
            await requestChannel.WriteAsync(op).ConfigureAwait(false);
        }
        catch (RetiredException ex)
        {
            // Try to get a better error message
            if (queueRunner.IsFaulted)
                await queueRunner.ConfigureAwait(false);

            throw new InvalidOperationException("Backend manager is stopped", ex);
        }
    }

    /// <summary>
    /// Calculates the hash of a file
    /// </summary>
    /// <param name="filename">The filename</param>
    /// <param name="options">The options</param>
    /// <returns>The hash</returns>
    protected static string CalculateFileHash(string filename, Options options)
    {
        using (var fs = System.IO.File.OpenRead(filename))
        using (var hasher = HashFactory.CreateHasher(options.FileHashAlgorithm))
            return Convert.ToBase64String(hasher.ComputeHash(fs));
    }

    /// <summary>
    /// Decrypts a file using the specified options
    /// </summary>
    /// <param name="tmpfile">The file to decrypt</param>
    /// <param name="filename">The name of the file. Used for detecting encryption algorithm if not specified in options or if it differs from the options</param>
    /// <param name="options">The Duplicati options</param>
    /// <returns>The decrypted file</returns>
    public TempFile DecryptFile(TempFile volume, string volume_name, Options options)
    {
        return GetOperation.DecryptFile(volume, volume_name, options);
    }

    /// <summary>
    /// Deletes a remote file
    /// </summary>
    /// <param name="remotename">The name of the remote file</param>
    /// <param name="size">The size of the remote file, for statistics</param>
    /// <param name="waitForComplete">True if the operation should wait for the file to actually be deleted. If this argument is false, the task will complete once the operation is queued</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task DeleteAsync(string remotename, long size, bool waitForComplete, CancellationToken cancelToken)
    {
        var op = new DeleteOperation(remotename, size, context, waitForComplete, cancelToken);
        await QueueTask(op).ConfigureAwait(false);
        await op.GetResult().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a file from the remote location
    /// </summary>
    /// <param name="remotename">The name of the remote file</param>
    /// <param name="hash">The hash of the remote file, for verification</param>
    /// <param name="size">The size of the remote file, for verification</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>A temporary file with the contents of the remote file</returns>
    public async Task<TempFile> GetAsync(string remotename, string hash, long size, CancellationToken cancelToken)
    {
        var op = new GetOperation(remotename, size, context, cancelToken)
        {
            Hash = hash,
            Decrypt = true
        };
        await QueueTask(op).ConfigureAwait(false);
        (var file, var _, var downloadSize) = await op.GetResult().ConfigureAwait(false);
        LastReadSize = downloadSize;
        return file;
    }

    /// <summary>
    /// Gets a file from the remote location without decrypting it
    /// </summary>
    /// <param name="remotename">The name of the remote file</param>
    /// <param name="hash">The hash of the remote file, for verification</param>
    /// <param name="size">The size of the remote file, for verification</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>A temporary file with the contents of the remote file</returns>
    public async Task<TempFile> GetDirectAsync(string remotename, string hash, long size, CancellationToken cancelToken)
    {
        var op = new GetOperation(remotename, size, context, cancelToken)
        {
            Hash = hash,
            Decrypt = false
        };
        await QueueTask(op).ConfigureAwait(false);
        (var file, var _, var downloadSize) = await op.GetResult().ConfigureAwait(false);
        LastReadSize = downloadSize;
        return file;
    }

    /// <summary>
    /// Gets quota information from the backend
    /// </summary>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The quota information</returns>
    public async Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken)
    {
        var op = new QuotaInfoOperation(context, cancelToken);
        await QueueTask(op).ConfigureAwait(false);
        return await op.GetResult().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a file from the remote location, along with the hash and size of the file
    /// </summary>
    /// <param name="remotename">The name of the remote file</param>
    /// <param name="hash">The hash of the remote file, or null if not known</param>
    /// <param name="size">The size of the remote file, or -1 if not known</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>A tuple containing the temporary file, the hash of the file, and the size of the file</returns>
    public async Task<(TempFile File, string Hash, long Size)> GetWithInfoAsync(string remotename, string hash, long size, CancellationToken cancelToken)
    {
        var op = new GetOperation(remotename, size, context, cancelToken)
        {
            Hash = hash,
            Decrypt = true
        };
        await QueueTask(op).ConfigureAwait(false);
        (var file, var downloadHash, var downloadSize) = await op.GetResult().ConfigureAwait(false);
        LastReadSize = downloadSize;
        return (file, downloadHash, downloadSize);
    }

    /// <summary>
    /// Lists files on the remote destination
    /// </summary>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The list of files</returns>
    public async Task<IEnumerable<Interface.IFileEntry>> ListAsync(CancellationToken cancelToken)
    {
        var op = new ListOperation(context, cancelToken);
        await QueueTask(op).ConfigureAwait(false);
        return await op.GetResult().ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads a volume to the remote location
    /// </summary>
    /// <param name="volume">The volume to upload</param>
    /// <param name="indexVolume">The index volume to upload, if any</param>
    /// <param name="indexVolumeFinished">The callback to call when the index volume is finished</param>
    /// <param name="waitForComplete">True if the operation should wait for the file to actually be uploaded. If this argument is false, the task will complete once the operation is queued</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task PutAsync(VolumeWriterBase volume, IndexVolumeWriter? indexVolume, Action? indexVolumeFinished, bool waitForComplete, CancellationToken cancelToken)
    {
        volume.Close();

        var op = new PutOperation(volume.RemoteFilename, context, waitForComplete, cancelToken)
        {
            LocalTempfile = volume.TempFile,
            OriginalIndexFile = indexVolume,
            Unencrypted = false,
            TrackedInDb = true,
            IndexVolumeFinishedCallback = indexVolumeFinished
        };

        // Prepare encryption
        op.StartEncryptionAndHashing();
        await QueueTask(op).ConfigureAwait(false);
        await op.GetResult().ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads a verification file to the remote location without encryption
    /// </summary>
    /// <param name="remotename">The name of the remote file</param>
    /// <param name="tempFile">The temporary file to upload</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task PutVerificationFileAsync(string remotename, TempFile tempFile, CancellationToken cancelToken)
    {
        var op = new PutOperation(remotename, context, true, cancelToken)
        {
            LocalTempfile = tempFile,
            Unencrypted = true, // Avoid encrypting
            TrackedInDb = false, // Not tracked
            OriginalIndexFile = null,
            IndexVolumeFinishedCallback = null
        };

        // Sets the task as already completed
        op.StartEncryptionAndHashing();
        await QueueTask(op).ConfigureAwait(false);
        await op.GetResult().ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the backend queue to be empty and flushes the database messages
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task WaitForEmptyAsync(LocalDatabase database, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        context.Database.FlushPendingMessages(database, transaction);

        var op = new WaitForEmptyOperation(context, cancellationToken);
        await QueueTask(op).ConfigureAwait(false);
        await op.GetResult().ConfigureAwait(false);

        context.Database.FlushPendingMessages(database, transaction);
    }

    /// <summary>
    /// Stops the backend manager and flushes any pending messages to the database
    /// </summary>
    /// <param name="database">The database to write pending messages to</param>
    /// <param name="transaction">The transaction to use, if any</param>
    public async Task StopRunnerAndFlushMessages(LocalDatabase database, IDbTransaction? transaction)
    {
        await requestChannel.RetireAsync().ConfigureAwait(false);
        context.Database.FlushPendingMessages(database, transaction);
        if (queueRunner.IsFaulted)
            Logging.Log.WriteWarningMessage(LOGTAG, "BackendManagerShutdown", queueRunner.Exception, "Backend manager queue runner crashed");
    }

    /// <summary>
    /// Stops the backend manager and discards any pending messages
    /// </summary>
    public void StopRunnerAndDiscardMessages()
    {
        requestChannel.RetireAsync().Await();
        if (queueRunner.IsFaulted)
            Logging.Log.WriteWarningMessage(LOGTAG, "BackendManagerShutdown", queueRunner.Exception, "Backend manager queue runner crashed");
        context.Database.ClearPendingMessages();
    }

    /// <summary>
    /// Performs a download of the files specified, with pre-fetch to overlap the download and processing
    /// </summary>
    /// <param name="volumes">The volumes to download</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The downloaded files and the volume they came from</returns>
    public async IAsyncEnumerable<(TempFile File, string Hash, long Size, string Name)> GetFilesOverlappedAsync(IEnumerable<IRemoteVolume> volumes, [EnumeratorCancellation] CancellationToken cancelToken)
    {
        var prevVolume = volumes.FirstOrDefault();
        if (prevVolume == null)
            yield break;

        // Get the first volume, so we do not have pending parallel transfers
        var prevResult = await GetWithInfoAsync(prevVolume.Name, prevVolume.Hash, prevVolume.Size, cancelToken);

        foreach (var volume in volumes.Skip(1))
        {
            // Prepare the next volume, while processing the previous one
            var nextTask = GetWithInfoAsync(volume.Name, volume.Hash, volume.Size, cancelToken);

            // Assuming we do not throw while yielding, otherwise we would need to dispose nextTask
            yield return (prevResult.File, prevResult.Hash, prevResult.Size, prevVolume.Name);
            prevResult.File.Dispose();

            // Set up for next iteration
            prevVolume = volume;
            prevResult = await nextTask;
        }

        // Return the last result
        yield return (prevResult.File, prevResult.Hash, prevResult.Size, prevVolume.Name);
        prevResult.File.Dispose();
    }

    /// <summary>
    /// Disposes the backend manager
    /// </summary>
    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        requestChannel.RetireAsync().Await();
        context.Database.FlushMessagesToLog();

        if (!queueRunner.IsCompleted)
        {
            Task.WhenAny(queueRunner, Task.Delay(1000)).Await();

            if (!queueRunner.IsCompleted)
                Logging.Log.WriteWarningMessage(LOGTAG, "BackendManagerShutdown", null, "Backend manager queue runner did not stop");
            if (queueRunner.IsFaulted)
                Logging.Log.WriteWarningMessage(LOGTAG, "BackendManagerShutdown", queueRunner.Exception, "Backend manager queue runner crashed");

        }
    }
}
