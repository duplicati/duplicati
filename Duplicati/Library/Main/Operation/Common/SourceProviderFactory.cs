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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Snapshots;
using Duplicati.Library.SourceProvider;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// Factory for creating source providers from a list of source paths.
    /// This logic is shared between backup and sync operations to ensure
    /// consistent source enumeration behavior.
    /// </summary>
    public static class SourceProviderFactory
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType(typeof(SourceProviderFactory));

        /// <summary>
        /// Gets a single source provider for the given sources
        /// </summary>
        /// <param name="sources">The sources to get providers for</param>
        /// <param name="options">The options to use</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The combined source provider</returns>
        public static async Task<ISourceProvider> GetSourceProviderAsync(IEnumerable<string> sources, Options options, CancellationToken cancellationToken)
            => Combiner.Combine(await GetSourceProvidersAsync(sources, options, cancellationToken));

        /// <summary>
        /// Gets a snapshot service for the given sources
        /// </summary>
        /// <param name="sources">The sources to get the snapshot for</param>
        /// <param name="options">The options to use</param>
        /// <returns>The snapshot service</returns>
        public static ISnapshotService GetFileSnapshotService(IEnumerable<string> sources, Options options)
        {
            try
            {
                if (options.SnapShotStrategy != Options.OptimizationStrategy.Off)
                    return SnapshotUtility.CreateSnapshot(sources, options.RawOptions, options.SymlinkPolicy == Options.SymlinkStrategy.Follow);
            }
            catch (Exception ex)
            {
                if (options.SnapShotStrategy == Options.OptimizationStrategy.Required)
                    throw new UserInformationException(Strings.Common.SnapshotFailedError(ex.Message, PermissionHelper.HasSnapshotPrivilege()), "SnapshotFailed", ex);
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.On)
                    Log.WriteWarningMessage(LOGTAG, "SnapshotFailed", ex, Strings.Common.SnapshotFailedError(ex.Message, PermissionHelper.HasSnapshotPrivilege()));
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.Auto)
                    Log.WriteInformationMessage(LOGTAG, "SnapshotFailed", Strings.Common.SnapshotFailedError(ex.Message, PermissionHelper.HasSnapshotPrivilege()));
            }

            var useSeBackup = false;
            if (options.BackupReadStrategy != Options.OptimizationStrategy.Off)
            {
                if (!OperatingSystem.IsWindows() || !PermissionHelper.HasSeBackupPrivilege())
                {
                    if (options.BackupReadStrategy == Options.OptimizationStrategy.Required)
                        throw new UserInformationException(Strings.Common.BackupReadNotAvailable, "BackupReadNotAvailable");
                    else if (options.BackupReadStrategy == Options.OptimizationStrategy.On)
                        Log.WriteWarningMessage(LOGTAG, "BackupReadNotAvailable", null, Strings.Common.BackupReadNotAvailable);
                    else
                        Log.WriteInformationMessage(LOGTAG, "BackupReadNotAvailable", Strings.Common.BackupReadNotAvailable);
                }
                else
                {
                    useSeBackup = true;
                }
            }

            return SnapshotUtility.CreateNoSnapshot(sources, options.IgnoreAdvisoryLocking, options.SymlinkPolicy == Options.SymlinkStrategy.Follow, useSeBackup, options.HandleMacOSPhotoLibrary, options.MacOSPhotoLibraryPath);
        }

        /// <summary>
        /// Gets all source providers for the given sources
        /// </summary>
        /// <param name="sources">The sources to get providers for</param>
        /// <param name="options">The options to use</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The source providers</returns>
        public static async Task<List<ISourceProvider>> GetSourceProvidersAsync(IEnumerable<string> sources, Options options, CancellationToken cancellationToken)
        {
            // Group the sources by their type, so we can combine all snapshot paths into a single snapshot
            var sourceTypes = sources.GroupBy(x => x.StartsWith("@") ? "@" : Duplicati.Library.Utility.Utility.GuessScheme(x) ?? "file", StringComparer.OrdinalIgnoreCase);

            // To avoid leaking snapshot instances, we create all instances first and then dispose them if an exception occurs
            // The number of instances is expected to be low, so the memory overhead is acceptable
            var results = new List<ISourceProvider>();
            try
            {
                foreach (var entry in sourceTypes)
                {
                    if ("file".Equals(entry.Key, StringComparison.OrdinalIgnoreCase))
                        results.Add(new LocalFileSource(GetFileSnapshotService(entry, options)));
                    else if ("vss".Equals(entry.Key, StringComparison.OrdinalIgnoreCase) || "lvm".Equals(entry.Key, StringComparison.OrdinalIgnoreCase))
                        results.Add(new LocalFileSource(SnapshotUtility.CreateSnapshot(entry, options.RawOptions, options.SymlinkPolicy == Options.SymlinkStrategy.Follow)));
                    else if ("@".Equals(entry.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var url in entry)
                        {
                            var sanitizedUrl = Duplicati.Library.Utility.Utility.GetUrlWithoutCredentials(url);
                            var m = Regex.Match(url, @"^@(?<mountpoint>[^|]+)\|(?<url>.+)$", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                var mountpoint = m.Groups["mountpoint"].Value;

                                if (mountpoint.Any(x => Path.GetInvalidPathChars().Contains(x)))
                                    throw new UserInformationException(string.Format("The mountpoint \"{0}\" contains invalid characters", mountpoint), "InvalidMountpoint");
                                if (!Path.IsPathRooted(mountpoint))
                                    throw new UserInformationException(string.Format("The mountpoint \"{0}\" is not a valid rooted mountpoint", mountpoint), "InvalidMountpoint");

                                var normalizedMountpoint = Duplicati.Library.Common.IO.Util.AppendDirSeparator(mountpoint);
                                var backendurl = m.Groups["url"].Value;

                                ISourceProvider provider;
                                try
                                {
                                    provider = await SourceProviderLoader.GetSourceProvider(backendurl, Path.GetFullPath(normalizedMountpoint), options.RawOptions, cancellationToken).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    if (options.AllowMissingSource)
                                    {
                                        Log.WriteWarningMessage(LOGTAG, "SourceProviderFailed", ex, "Failed to load source provider for \"{0}\"", sanitizedUrl);
                                        continue;
                                    }

                                    throw new UserInformationException($"Failed to load source provider for \"{sanitizedUrl}\": {ex.Message}", "SourceProviderFailed", ex);
                                }

                                // Don't accept missing providers
                                results.Add(provider ?? throw new UserInformationException($"The source \"{sanitizedUrl}\" is not supported", "SourceNotSupported"));
                            }
                            else
                                throw new UserInformationException($"The source \"{sanitizedUrl}\" is not a supported format", "SourceFormatNotSupported");
                        }
                    }
                    else
                        throw new UserInformationException($"The source type \"{entry.Key}\" is not supported", "SourceTypeNotSupported");
                }
            }
            catch
            {
                foreach (var provider in results)
                    (provider as IDisposable)?.Dispose();

                throw;
            }

            if (results.Count == 0)
                throw new UserInformationException("No sources were available for the backup", "NoSourcesAvailable");

            return results;
        }
    }
}
