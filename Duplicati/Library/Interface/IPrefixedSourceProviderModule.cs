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

namespace Duplicati.Library.Interface;

/// <summary>
/// A source provider module that is activated by a path prefix (e.g. <c>%HYPERV%</c>)
/// instead of a URL scheme. Providers implementing this interface are matched
/// against the source paths, and all matched paths are passed to a single provider
/// instance created by <see cref="CreateForSources"/>.
/// </summary>
/// <remarks>
/// Instances registered in the module catalog are used as descriptors only;
/// the actual provider instance is created via <see cref="CreateForSources"/>.
/// <para>
/// The prefix only selects the content; the entries the provider produces are
/// placed below <see cref="ISourceProvider.MountedPath"/>, which is a rooted path.
/// </para>
/// </remarks>
public interface IPrefixedSourceProviderModule : ISourceProviderModule
{
    /// <summary>
    /// The prefix that selects this provider's content in a source path (e.g. <c>%HYPERV%</c>)
    /// </summary>
    string SourcePrefix { get; }

    /// <summary>
    /// The metadata key that holds the local path an entry below
    /// <see cref="ISourceProvider.MountedPath"/> was read from. The virtual folders
    /// of the provider do not have it, as they have no local counterpart.
    /// </summary>
    string OriginalPathMetadataKey { get; }

    /// <summary>
    /// Translates a path written with <see cref="SourcePrefix"/> (e.g. <c>%MSSQL%\server\instance\database</c>)
    /// to the virtual path the provider stores the matching entry under, so a filter
    /// written the same way as the sources matches the stored entries.
    /// A path that names a virtual folder is translated with a trailing directory separator.
    /// </summary>
    /// <param name="sourcePath">The path, starting with <see cref="SourcePrefix"/></param>
    /// <returns>The virtual path, or <c>null</c> if the path does not name an entry the provider stores</returns>
    string? TranslateSourcePath(string sourcePath);

    /// <summary>
    /// Checks whether the given source path is handled by this provider
    /// (e.g. equals the prefix or starts with the prefix followed by a directory separator)
    /// </summary>
    /// <param name="source">The source path to check</param>
    /// <returns>True if the source path is handled by this provider</returns>
    bool MatchesSource(string source);

    /// <summary>
    /// True if the provider is supported on the current platform
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Applies option changes required by the provider before the snapshot is created
    /// (e.g. forcing the snapshot policy, removing VSS writers from the exclude list).
    /// This method is called on the descriptor instance before <see cref="CreateForSources"/>.
    /// </summary>
    /// <param name="sources">The source paths that matched this provider</param>
    /// <param name="options">The commandline options, which may be modified</param>
    void PrepareOptions(IReadOnlyList<string> sources, IDictionary<string, string?> options);

    /// <summary>
    /// Creates a source provider instance that serves the given matched source paths
    /// </summary>
    /// <param name="sources">The source paths that matched this provider</param>
    /// <param name="options">The commandline options</param>
    /// <returns>The source provider instance</returns>
    ISourceProviderModule CreateForSources(IReadOnlyList<string> sources, IReadOnlyDictionary<string, string?> options);
}
