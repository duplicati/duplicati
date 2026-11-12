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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// The handoff of a recording session to one backup run. The backup calls
/// <see cref="DrainChangeListAsync"/> once its snapshot exists; the service
/// then closes the session and writes the list file this instance owns.
/// </summary>
public sealed class Handoff : IChangeListProvider, IDisposable
{
    private static readonly string LOGTAG = Log.LogTagFromType<Handoff>();

    /// <summary>
    /// How many times deleting the list file is retried when the backup still holds it
    /// </summary>
    private const int DELETE_ATTEMPTS = 5;

    private readonly ChangeTrackingService _service;
    private int _disposed;

    /// <summary>
    /// The backup id
    /// </summary>
    public string BackupId { get; }

    /// <summary>
    /// The hash of the backup settings for this run
    /// </summary>
    public string SettingsHash { get; }

    /// <summary>
    /// Whether the closed session can be handed to the backup. When false the drain
    /// still rotates the session, so the next run can be incremental, but returns null.
    /// </summary>
    public bool Usable { get; }

    /// <summary>
    /// Why the backup scans everything, or null when the session is usable
    /// </summary>
    public string? FullScanReason { get; }

    /// <summary>
    /// Whether the backup was handed a list
    /// </summary>
    public bool Drained { get; internal set; }

    /// <summary>
    /// Whether the drain closed the recording session
    /// </summary>
    public bool Rotated { get; internal set; }

    /// <summary>
    /// Whether the run was completed; a drain arriving after that does nothing
    /// </summary>
    internal bool Completed { get; set; }

    /// <summary>
    /// Serializes the drain and the completion
    /// </summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>
    /// The session that was closed by the drain and handed to the backup, or null
    /// </summary>
    public long? ClosedSessionId { get; internal set; }

    /// <summary>
    /// The list file handed to the backup, or null
    /// </summary>
    public string? ListPath { get; internal set; }

    /// <summary>
    /// The number of entries in the list file
    /// </summary>
    public long EntryCount { get; internal set; }

    internal Handoff(ChangeTrackingService service, string backupId, string settingsHash, bool usable, string? fullScanReason)
    {
        _service = service;
        BackupId = backupId;
        SettingsHash = settingsHash;
        Usable = usable;
        FullScanReason = fullScanReason;
    }

    /// <inheritdoc />
    public Task<ChangeListHandoff?> DrainChangeListAsync(DateTime referenceTimeUtc, CancellationToken cancellationToken)
        => _service.DrainAsync(this, referenceTimeUtc, cancellationToken);

    /// <summary>
    /// Deletes the list file. The backup may still hold it open in the out-of-process
    /// case, so the deletion is retried in the background before giving up.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var path = ListPath;
        if (path == null)
            return;

        if (TryDelete(path))
            return;

        Task.Run(async () =>
        {
            for (var attempt = 1; attempt < DELETE_ATTEMPTS; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                if (TryDelete(path))
                    return;
            }

            Log.WriteWarningMessage(LOGTAG, "ChangeListDeleteFailed", null, "Could not delete the change list file {0}", path);
        });
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
