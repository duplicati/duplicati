// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or 
// sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using Duplicati.Library.ChangeTracking;
using Duplicati.Server.Database;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Stops the file watcher of a backup whenever the backup is saved or deleted, whichever
/// way that happens: the web UI, the remote controller, an import or a delete task. A saved
/// backup may have other sources or settings, or no longer use the watcher, so a later run
/// starts over, and a deleted backup must not keep its watcher running.
/// </summary>
public static class ChangeTrackingBackupListener
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType(typeof(ChangeTrackingBackupListener));

    /// <summary>
    /// Subscribes the change tracking service to the changes of the backups
    /// </summary>
    /// <param name="connection">The server database connection</param>
    /// <param name="changeTracking">The change tracking service</param>
    public static void Attach(Connection connection, IChangeTrackingService changeTracking)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(changeTracking);

        // The change may be raised while the connection lock is held, and stopping waits for a
        // watcher that is starting, so it runs on its own. A run that starts in between at worst
        // loses its list and scans all files.
        connection.BackupChanged += backupId => _ = Task.Run(() => StopAsync(changeTracking, backupId));
    }

    /// <summary>
    /// Stops the watcher of a backup, logging a failure
    /// </summary>
    /// <param name="changeTracking">The change tracking service</param>
    /// <param name="backupId">The backup id</param>
    private static async Task StopAsync(IChangeTrackingService changeTracking, string backupId)
    {
        try
        {
            await changeTracking.StopAsync(backupId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Library.Logging.Log.WriteWarningMessage(LOGTAG, "ChangeTrackingStopFailed", ex, "Could not stop the file watcher for backup {0}", backupId);
        }
    }
}
