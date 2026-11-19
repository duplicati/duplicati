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

using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.Library.RestAPI;

/// <summary>
/// Deleting a job together with the remote files of the destinations chosen for it.
/// The main destination is named <see cref="PrimaryKey"/>, a secondary destination by its url key.
/// </summary>
public static class RemoteDestinationDelete
{
    /// <summary>
    /// The name that selects the main destination of the job
    /// </summary>
    public const string PrimaryKey = "primary";

    /// <summary>
    /// The query parameter of the delete request that lists the destinations
    /// </summary>
    public const string QueryParameter = "remote-destinations";

    /// <summary>
    /// The option of the delete task that carries the destinations, in the order they are deleted
    /// </summary>
    public const string TaskOption = "delete-remote-destinations";

    /// <summary>
    /// The API extension that signals a job can be deleted with the remote files of chosen destinations
    /// </summary>
    public const string ApiExtension = "v1:backup:delete-remote-destinations";

    /// <summary>
    /// Checks the destinations a delete request names, before anything is deleted.
    /// </summary>
    /// <param name="backup">The job</param>
    /// <param name="requested">The values of the query parameter, each a comma-separated list; null when the parameter is not given</param>
    /// <param name="deleteRemoteFiles">The value of the older <c>delete-remote-files</c> parameter, null when it is not given</param>
    /// <returns>The destinations to delete, the main destination first and then the secondary ones in the order of the job; null when the request does not name any, and the older parameter decides</returns>
    /// <exception cref="UserInformationException">When the destinations cannot be deleted as asked</exception>
    public static IReadOnlyList<string>? Resolve(IBackup backup, IEnumerable<string?>? requested, bool? deleteRemoteFiles)
    {
        if (requested == null)
            return null;

        var names = requested
            .Where(x => x != null)
            .SelectMany(x => x!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
            throw new UserInformationException("No destination is named. Leave out the parameter to keep the remote files of every destination.", "RemoteDestinationsEmpty");

        if (deleteRemoteFiles.HasValue)
            throw new UserInformationException($"The parameters delete-remote-files and {QueryParameter} cannot be given together.", "RemoteDestinationsConflict");

        if (backup.OperationType == OperationType.Sync)
            throw new UserInformationException("The remote files of a sync job are not deleted, as the destination holds a copy of the files and not a backup.", "RemoteDestinationsSyncJob");

        var secondary = (backup.AdditionalTargetURLs ?? []).ToList();
        if (secondary.Any(x => string.Equals(x.TargetUrlKey, PrimaryKey, StringComparison.OrdinalIgnoreCase)))
            throw new UserInformationException($"A secondary destination of the job has the key \"{PrimaryKey}\", so the destinations cannot be told apart.", "RemoteDestinationsAmbiguousKey");

        var unknown = names
            .Where(x => x != PrimaryKey && !secondary.Any(y => string.Equals(y.TargetUrlKey, x, StringComparison.Ordinal)))
            .ToList();
        if (unknown.Count > 0)
            throw new UserInformationException($"The job has no destination with the key: {string.Join(", ", unknown)}", "RemoteDestinationsUnknown");

        var all = new List<(string Key, string Url)> { (PrimaryKey, backup.TargetURL) };
        all.AddRange(secondary.Select(x => (x.TargetUrlKey, x.TargetUrl)));
        var selected = all.Where(x => names.Contains(x.Key)).ToList();

        // Deleting the files at one destination also deletes them for another destination at the same place
        var shared = selected
            .Where(x => all.Any(y => !names.Contains(y.Key) && LocationOf(y.Url) == LocationOf(x.Url)))
            .Select(x => x.Key)
            .ToList();
        if (shared.Count > 0)
            throw new UserInformationException($"These destinations are at the same place as a destination that is kept: {string.Join(", ", shared)}", "RemoteDestinationsSharedLocation");

        return selected.Select(x => x.Key).ToList();
    }

    /// <summary>
    /// The place a destination url points to, without credentials, options and case, so two urls
    /// that may point to the same place are treated as the same
    /// </summary>
    /// <param name="url">The url</param>
    /// <returns>The place</returns>
    internal static string LocationOf(string url)
    {
        var place = Utility.Utility.GetUrlWithoutCredentials(url ?? "");
        var query = place.IndexOf('?');
        if (query >= 0)
            place = place.Substring(0, query);
        return place.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    }
}

/// <summary>
/// The remote files of some of the chosen destinations could not be deleted, so the job and its database are kept
/// </summary>
/// <param name="failedDestinations">The destinations that failed</param>
public sealed class RemoteDestinationDeleteException(IReadOnlyList<string> failedDestinations)
    : UserInformationException($"The remote files could not be deleted from: {string.Join(", ", failedDestinations)}. The job and its local database are kept, so the delete can be started again.", "RemoteDestinationsDeleteFailed")
{
    /// <summary>
    /// The destinations whose remote files could not be deleted, by their key
    /// </summary>
    public IReadOnlyList<string> FailedDestinations { get; } = failedDestinations;
}
