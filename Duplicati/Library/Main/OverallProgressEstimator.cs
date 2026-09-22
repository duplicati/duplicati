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

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Derives an overall progress value in the range <c>[0, 1]</c> from the raw
    /// counters exposed by <see cref="IOperationProgress"/>.
    ///
    /// Most long-running phases (notably the backup file-processing phase) never
    /// call <see cref="OperationProgressUpdater.UpdateProgress"/>, so the stored
    /// overall progress stays at zero. The web UI works around this by computing
    /// a percentage from the byte counters and applying fixed values for the later
    /// phases. This helper mirrors that logic so external consumers, such as the
    /// report modules, receive a meaningful value.
    /// </summary>
    public static class OverallProgressEstimator
    {
        /// <summary>
        /// The progress reported while processing files is capped at this value,
        /// leaving room for the finalize, compact and verification phases.
        /// </summary>
        public const float ProcessingFilesCap = 0.90f;

        /// <summary>
        /// Estimates the overall progress for the given progress counters.
        /// </summary>
        /// <param name="phase">The current operation phase.</param>
        /// <param name="reportedProgress">The overall progress explicitly reported by the operation, if any.</param>
        /// <param name="filesProcessed">The number of files processed so far.</param>
        /// <param name="fileSizeProcessed">The number of bytes processed so far, not including the current file.</param>
        /// <param name="fileSize">The total number of bytes to process.</param>
        /// <param name="countingFiles">True if the file count and size are still being determined.</param>
        /// <param name="currentFileOffset">The offset reached in the file currently being processed.</param>
        /// <param name="currentFileComplete">True if the current file has been fully processed.</param>
        /// <returns>An overall progress value in the range <c>[0, 1]</c>.</returns>
        public static float Estimate(
            OperationPhase phase,
            float reportedProgress,
            long filesProcessed,
            long fileSizeProcessed,
            long fileSize,
            bool countingFiles,
            long currentFileOffset,
            bool currentFileComplete)
        {
            switch (phase)
            {
                case OperationPhase.Backup_ProcessingFiles:
                case OperationPhase.Restore_DownloadingRemoteFiles:
                    if (countingFiles || fileSize <= 0 || filesProcessed <= 0)
                        return 0f;

                    // Bytes from the in-flight file are not yet counted in fileSizeProcessed
                    var unaccountedBytes = currentFileComplete ? 0 : Math.Max(0, currentFileOffset);
                    var pg = (double)(fileSizeProcessed + unaccountedBytes) / fileSize;
                    return (float)Math.Min(ProcessingFilesCap, Math.Max(0, pg));

                case OperationPhase.Backup_Finalize:
                case OperationPhase.Backup_WaitForUpload:
                    return 0.90f;

                case OperationPhase.Backup_Delete:
                case OperationPhase.Backup_Compact:
                    return 0.95f;

                case OperationPhase.Backup_VerificationUpload:
                case OperationPhase.Backup_PostBackupVerify:
                    return 0.98f;

                case OperationPhase.Backup_Complete:
                case OperationPhase.Restore_Complete:
                    return 1f;

                default:
                    return Clamp(reportedProgress);
            }
        }

        /// <summary>
        /// Clamps a progress value to the range <c>[0, 1]</c>, treating NaN as zero.
        /// </summary>
        private static float Clamp(float value)
        {
            if (float.IsNaN(value))
                return 0f;
            return Math.Min(1f, Math.Max(0f, value));
        }
    }
}
