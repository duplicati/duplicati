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

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Restore
{
    /// <summary>
    /// Class for keeping track of the maximum processing time of a block
    /// request making a roundtrip to/from the BlockManager.
    ///
    /// The times are not Interlocked as reads and writes are atomic for int
    /// types and exact accuracy is not required.
    ///
    /// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/variables#96-atomicity-of-variable-references
    ///
    /// The worst case is that the reads are stale by one update, which is
    /// acceptable for this use case. The same applies to the MaxProcessingTime
    /// property, which means that the BlockManager may use a slightly stale
    /// value when checking for timeouts, which is also acceptable.
    /// </summary>
    internal class DeadlockTimer
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG =
            Logging.Log.LogTagFromType<DeadlockTimer>();
        /// <summary>
        /// Five minutes in milliseconds.
        /// </summary>
        public static int initial_threshold = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
        /// <summary>
        /// Cancellation token for stopping the deadlock timer.
        /// </summary>
        public static CancellationTokenSource Token = new();
        /// <summary>
        /// Maximum processing time (in milliseconds) recorded for any block
        /// request.
        /// </summary>
        public static int MaxProcessingTime = initial_threshold;

        /// <summary>
        /// Runs the deadlock timer process. It runs every second and updates
        /// the maximum processing time based on the maximum processing times
        /// of the active VolumeDownloaders, VolumeDecryptors and
        /// VolumeDecompressors.
        /// </summary>
        /// <remarks>It will keep running until the cancellation token
        /// `DeadlockTimer.token` is cancelled.</remarks>
        /// <returns>An awaitable task.</returns>
        public static async Task Run()
        {
            try
            {
                while (!Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), Token.Token)
                        .ConfigureAwait(false);

                    int download = VolumeDownloader.MaxProcessingTimes.Max();
                    int decrypt = VolumeDecryptor.MaxProcessingTimes.Max();
                    int decompress = VolumeDecompressor.MaxProcessingTimes.Max();

                    MaxProcessingTime = Math.Max(
                        initial_threshold,
                        (download + decrypt + decompress) * 2
                    );
                }
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
        }
    }
}