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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Duplicati.Library.Interface;

/// <summary>
/// A restore destination that is told which files a restore writes, before it writes any of them.
/// A destination implements it when a selection can be wrong for it as a whole, so it can refuse
/// the selection before it changes anything.
/// </summary>
public interface IRestoreSelectionReceiver
{
    /// <summary>
    /// Gives the destination the files the restore writes. Called once, after the target paths
    /// are set and before any file is opened.
    /// </summary>
    /// <param name="targetPaths">The target paths of the files, as the destination is given them when they are opened</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>An awaitable task; a failure stops the restore before anything is written</returns>
    /// <remarks>Be aware that implementing this means that all paths are loaded into memory, so only implement this if the expected number of paths is low.</remarks>
    Task SetRestoreSelection(IReadOnlyCollection<string> targetPaths, CancellationToken cancel);
}
