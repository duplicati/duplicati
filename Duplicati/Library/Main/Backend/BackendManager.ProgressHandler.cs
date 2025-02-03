using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending QuotaInfo operation
    /// </summary>
    private sealed record ProgressHandler(Options Options, IBackendWriter Stats, ITaskReader TaskReader)
    {
        /// <summary>
        /// Stores the last throttle values
        /// </summary>
        /// <param name="Upload">The upload throttle value</param>
        /// <param name="Download">The download throttle value</param>
        private sealed record ThrottleValues(string? Upload, string? Download);

        /// <summary>
        /// The last throttle values
        /// </summary>
        private ThrottleValues lastThrottleValue = new(string.Empty, string.Empty);

        /// <summary>
        /// Handles the progress of a throttled stream
        /// </summary>
        /// <param name="ts">The throttled stream</param>
        /// <param name="pg">The progress</param>
        /// <param name="filename">The filename</param>
        public void HandleProgress(ThrottledStream ts, long pg, string filename)
        {
            // This pauses and throws on cancellation, but ignores stop
            TaskReader.TransferRendevouz().Await();

            var prev = lastThrottleValue;

            // Update the throttle speeds if they have changed
            Options.RawOptions.TryGetValue("throttle-upload", out var tmpUpload);
            Options.RawOptions.TryGetValue("throttle-download", out var tmpDownload);

            if (tmpUpload != prev.Upload || tmpDownload != prev.Download)
            {
                ts.WriteSpeed = Options.MaxUploadPrSecond;
                ts.ReadSpeed = Options.MaxDownloadPrSecond;
                lastThrottleValue = new(tmpUpload, tmpDownload);
            }

            Stats.BackendProgressUpdater.UpdateProgress(pg);
        }
    }
}