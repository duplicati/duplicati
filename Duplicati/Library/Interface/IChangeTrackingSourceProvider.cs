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

namespace Duplicati.Library.Interface;

/// <summary>
/// A source provider that can enumerate only the entries that changed since the
/// previous backup, carrying state between runs. The backup stores the state
/// the provider exports with the fileset, hands it back before the next
/// enumeration, and copies every path the provider did not examine from the
/// previous fileset.
/// </summary>
public interface IChangeTrackingSourceProvider : ISourceProvider
{
    /// <summary>
    /// A stable key for this provider instance, used to scope the stored state.
    /// For a remote source given as "@mountpoint|url" this is the mount point.
    /// </summary>
    string ChangeTrackingScope { get; }

    /// <summary>
    /// Called before enumeration with the rows stored for the previous fileset.
    /// An empty set means that nothing is known and every entry must be walked in full.
    /// The call also tells the provider that it is used for a backup, so it should
    /// export state when the enumeration completes.
    /// </summary>
    /// <param name="state">The state rows stored by the previous backup</param>
    void LoadChangeTrackingState(IReadOnlyDictionary<string, string> state);

    /// <summary>
    /// Called after a completed enumeration. Returns the rows to store for the new fileset.
    /// Keys that the provider does not re-export are dropped. The call is not made when the
    /// backup was cut short: a partial backup stores no state, and the next backup is handed
    /// an empty set and walks everything.
    /// </summary>
    /// <returns>The state rows to store</returns>
    IReadOnlyDictionary<string, string> ExportChangeTrackingState();

    /// <summary>
    /// The carry-forward predicate. Returns <c>true</c> when the path was examined
    /// in this run, so that its absence from the enumeration means it was deleted,
    /// and <c>false</c> when the path was not visited and must be copied from the
    /// previous fileset. It is called once for every path in the previous fileset
    /// that the current enumeration did not produce, so it must be cheap.
    /// </summary>
    /// <param name="path">The path from the previous fileset</param>
    /// <returns><c>true</c> if the path was examined in this run</returns>
    bool WasPathEnumerated(string path);

    /// <summary>
    /// Whether a folder produced by this enumeration lists only the entries that changed,
    /// so that a folder listing nothing cannot be told apart from an empty one and must not
    /// be dropped as empty. It is called for folders the enumeration produced, after they
    /// were enumerated.
    /// </summary>
    /// <param name="folderPath">The folder path</param>
    /// <returns><c>true</c> if the folder was enumerated incrementally</returns>
    bool IsIncrementallyEnumeratedFolder(string folderPath);

    /// <summary>
    /// Registers the observer that is told how each folder is walked, as the enumeration
    /// reaches it. The backup uses it to estimate how much of the previous fileset the
    /// enumeration produces again: the entries below a folder reported as
    /// <see cref="FolderWalkMode.Incremental"/> are expected to be carried forward, and a
    /// later <see cref="FolderWalkMode.Full"/> report for the same folder takes that back.
    /// Only a change in how a folder is walked is reported, and a folder below one that was
    /// reported may be reported too. The observer is registered after the state is loaded
    /// and before the enumeration starts; it is called from the enumeration, possibly from
    /// several threads at once, and must return quickly.
    /// </summary>
    /// <param name="observer">The observer, called with the folder path and how it is walked</param>
    void SetFolderWalkObserver(Action<string, FolderWalkMode> observer);
}
