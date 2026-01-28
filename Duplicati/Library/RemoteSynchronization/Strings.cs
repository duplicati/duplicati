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

using Duplicati.Library.Localization.Short;

namespace RemoteSynchronization
{
    /// <summary>
    /// Localized strings for the Remote Synchronization tool.
    /// </summary>
    public static class Strings
    {
        /// <summary>
        /// Root command description.
        /// </summary>
        public static string RootCommandDescription { get { return LC.L(@"Remote Synchronization Tool

This tool synchronizes two remote backends. The tool assumes that the intent is
to have the destination match the source.

If the destination has files that are not in the source, they will be deleted
(or renamed if the retention option is set).

If the destination has files that are also present in the source, but the files
differ in size, or if the source files have a newer (more recent) timestamp,
the destination files will be overwritten by the source files. Given that some
backends do not allow for metadata or timestamp modification, and that the tool
is run after backup, the destination files should always have a timestamp that
is newer (or the same if run promptly) compared to the source files.

If the force option is set, the destination will be overwritten by the source,
regardless of the state of the files. It will also skip the initial comparison,
and delete (or rename) all files in the destination.

If the verify option is set, the files will be downloaded and compared after
uploading to ensure that the files are correct. Files that already exist in the
destination will be verified before being overwritten (if they seemingly match).
"); } }

        /// <summary>
        /// The source backend string argument description.
        /// </summary>
        public static string SourceBackendDescription { get { return LC.L("The source backend string"); } }

        /// <summary>
        /// The destination backend string argument description.
        /// </summary>
        public static string DestinationBackendDescription { get { return LC.L("The destination backend string"); } }

        /// <summary>
        /// Auto-create folders option description.
        /// </summary>
        public static string AutoCreateFoldersDescription { get { return LC.L("Automatically create folders in the destination backend if they do not exist"); } }

        /// <summary>
        /// Backend retries option description.
        /// </summary>
        public static string BackendRetriesDescription { get { return LC.L("Number of times to recreate a backend on backend errors"); } }

        /// <summary>
        /// Backend retry delay option description.
        /// </summary>
        public static string BackendRetryDelayDescription { get { return LC.L("Delay in milliseconds between backend retries"); } }

        /// <summary>
        /// Backend retry with exponential backoff option description.
        /// </summary>
        public static string BackendRetryWithExponentialBackoffDescription { get { return LC.L("Use exponential backoff for backend retries, multiplying the delay by two for each failure."); } }

        /// <summary>
        /// Confirm option description.
        /// </summary>
        public static string ConfirmDescription { get { return LC.L("Automatically confirm the operation"); } }

        /// <summary>
        /// Dry-run option description.
        /// </summary>
        public static string DryRunDescription { get { return LC.L("Do not actually write or delete files. If not set here, the global options will be checked"); } }

        /// <summary>
        /// Destination options option description.
        /// </summary>
        public static string DstOptionsDescription { get { return LC.L("Options for the destination backend. Each option is a key-value pair separated by an equals sign, e.g. --dst-options key1=value1 key2=value2 [default: empty]"); } }

        /// <summary>
        /// Force option description.
        /// </summary>
        public static string ForceDescription { get { return LC.L("Force the synchronization"); } }

        /// <summary>
        /// Global options option description.
        /// </summary>
        public static string GlobalOptionsDescription { get { return LC.L("Global options all backends. May be overridden by backend specific options (src-options, dst-options). Each option is a key-value pair separated by an equals sign, e.g. --global-options key1=value1 key2=value2 [default: empty]"); } }

        /// <summary>
        /// Log file option description.
        /// </summary>
        public static string LogFileDescription { get { return LC.L("The log file to write to. If not set here, global options will be checked [default: \"\"]"); } }

        /// <summary>
        /// Log level option description.
        /// </summary>
        public static string LogLevelDescription { get { return LC.L("The log level to use. If not set here, global options will be checked"); } }

        /// <summary>
        /// Parse arguments only option description.
        /// </summary>
        public static string ParseArgumentsOnlyDescription { get { return LC.L("Only parse the arguments and then exit"); } }

        /// <summary>
        /// Progress option description.
        /// </summary>
        public static string ProgressDescription { get { return LC.L("Print progress to STDOUT"); } }

        /// <summary>
        /// Retention option description.
        /// </summary>
        public static string RetentionDescription { get { return LC.L("Toggles whether to keep old files. Any deletes will be renames instead"); } }

        /// <summary>
        /// Retry option description.
        /// </summary>
        public static string RetryDescription { get { return LC.L("Number of times to retry on errors"); } }

        /// <summary>
        /// Source options option description.
        /// </summary>
        public static string SrcOptionsDescription { get { return LC.L("Options for the source backend. Each option is a key-value pair separated by an equals sign, e.g. --src-options key1=value1 key2=value2 [default: empty]"); } }

        /// <summary>
        /// Verify contents option description.
        /// </summary>
        public static string VerifyContentsDescription { get { return LC.L("Verify the contents of the files to decide whether the pre-existing destination files should be overwritten"); } }

        /// <summary>
        /// Verify get after put option description.
        /// </summary>
        public static string VerifyGetAfterPutDescription { get { return LC.L("Verify the files after uploading them to ensure that they were uploaded correctly"); } }

    }
}
