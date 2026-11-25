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

#nullable enable

using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// Fits the number of volumes a backup keeps in the temporary folder to the free space there.
    /// Each compressor writes one volume at a time, and each upload holds one until it is sent, so
    /// a backup can hold up to (compressors + uploads) volumes at once. A temporary folder that is
    /// too small for them, such as a RAM disk, made the backup fail part way through (#4157).
    /// </summary>
    internal static class TemporarySpaceLimiter
    {
        /// <summary>
        /// The log tag
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(TemporarySpaceLimiter));

        /// <summary>
        /// The option that sets the number of compressors
        /// </summary>
        internal const string COMPRESSORS_OPTION = "concurrency-compressors";

        /// <summary>
        /// The option that sets the number of concurrent uploads
        /// </summary>
        internal const string UPLOADS_OPTION = "asynchronous-upload-limit";

        /// <summary>
        /// The numbers of compressors and uploads to run with
        /// </summary>
        /// <param name="Compressors">The number of compressors</param>
        /// <param name="Uploads">The number of concurrent uploads</param>
        /// <param name="Needed">The space the volumes need with these numbers</param>
        /// <param name="Fits">True if the volumes fit in the free space</param>
        internal sealed record Plan(int Compressors, int Uploads, long Needed, bool Fits);

        /// <summary>
        /// Works out how many compressors and uploads fit in the free space. Only numbers the
        /// user did not set are lowered, the larger one first, and never below one.
        /// </summary>
        /// <param name="freeSpace">The free space in the folder the volumes are written to</param>
        /// <param name="volumeSize">The size of a volume</param>
        /// <param name="compressors">The number of compressors</param>
        /// <param name="compressorsSet">True if the user set the number of compressors</param>
        /// <param name="uploads">The number of concurrent uploads</param>
        /// <param name="uploadsSet">True if the user set the number of uploads</param>
        /// <returns>The numbers to run with</returns>
        internal static Plan Decide(long freeSpace, long volumeSize, int compressors, bool compressorsSet, int uploads, bool uploadsSet)
        {
            long Needed(int c, int u) => (c + u) * volumeSize;

            while (Needed(compressors, uploads) > freeSpace)
            {
                var canLowerCompressors = !compressorsSet && compressors > 1;
                var canLowerUploads = !uploadsSet && uploads > 1;
                if (!canLowerCompressors && !canLowerUploads)
                    break;

                if (canLowerCompressors && (!canLowerUploads || compressors >= uploads))
                    compressors--;
                else
                    uploads--;
            }

            return new Plan(compressors, uploads, Needed(compressors, uploads), Needed(compressors, uploads) <= freeSpace);
        }

        /// <summary>
        /// Checks the free space in the folder the volumes are written to, and lowers the numbers of
        /// compressors and uploads in the options when the volumes would not fit
        /// </summary>
        /// <param name="options">The options of the backup, which are changed</param>
        /// <param name="backendUrl">The URL of the destination</param>
        /// <returns>The messages to log once the operation logs, empty if there is nothing to say</returns>
        internal static IReadOnlyList<string> Apply(Options options, string backendUrl)
        {
            var uploads = options.GetAsynchronousConcurrentUploadLimit(backendUrl);
            // Zero means no limit on uploads, so there is no number of volumes to fit
            if (uploads <= 0 || options.Dryrun)
                return [];

            var folder = options.AsynchronousUploadFolder;
            var space = Library.Utility.Utility.GetFreeSpaceForPath(folder);
            if (space == null)
                return [];

            var compressors = options.ConcurrencyCompressors;
            var compressorsSet = options.RawOptions.ContainsKey(COMPRESSORS_OPTION);
            var uploadsSet = options.RawOptions.ContainsKey(UPLOADS_OPTION) || options.RawOptions.ContainsKey("asynchronous-concurrent-upload-limit");
            var volumeSize = options.VolumeSize;

            var plan = Decide(space.Value.FreeSpace, volumeSize, compressors, compressorsSet, uploads, uploadsSet);
            if (plan.Compressors == compressors && plan.Uploads == uploads && plan.Fits)
                return [];

            if (plan.Compressors != compressors)
                options.RawOptions[COMPRESSORS_OPTION] = plan.Compressors.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (plan.Uploads != uploads)
                options.RawOptions[UPLOADS_OPTION] = plan.Uploads.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var free = Library.Utility.Utility.FormatSizeString(space.Value.FreeSpace);
            var messages = new List<string>();
            if (plan.Compressors != compressors || plan.Uploads != uploads)
                messages.Add(Strings.Controller.TemporarySpaceLowered(folder, free, Library.Utility.Utility.FormatSizeString(volumeSize), compressors, uploads, plan.Compressors, plan.Uploads));
            if (!plan.Fits)
                messages.Add(Strings.Controller.TemporarySpaceTooSmall(folder, free, Library.Utility.Utility.FormatSizeString(plan.Needed), plan.Compressors, plan.Uploads, COMPRESSORS_OPTION, UPLOADS_OPTION, "tempdir", "dblock-size"));

            return messages;
        }

        /// <summary>
        /// Writes the messages from <see cref="Apply"/> as warnings
        /// </summary>
        /// <param name="messages">The messages</param>
        internal static void Log(IReadOnlyList<string> messages)
        {
            foreach (var message in messages)
                Logging.Log.WriteWarningMessage(LOGTAG, "TemporarySpaceLow", null, message);
        }
    }
}
