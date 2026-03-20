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

using System;
using Duplicati.Library.Interface;

#nullable enable

namespace Duplicati.Library.Main;

/// <summary>
/// Extended callback module interface that provides access to the operation progress updater.
/// Modules that need to report progress (e.g., remote synchronization) should implement this
/// interface instead of <see cref="IGenericCallbackModule"/>.
/// </summary>
public interface IGenericCallbackModuleWithProgress : IGenericCallbackModule
{
    /// <summary>
    /// Called when the operation finishes, with access to the progress and backend updaters.
    /// This is called instead of <see cref="IGenericCallbackModule.OnFinish"/> when
    /// the updaters are available.
    /// </summary>
    /// <param name="result">The result object for the operation.</param>
    /// <param name="exception">The exception that stopped the operation, or null.</param>
    /// <param name="progressUpdater">The progress updater for reporting operation phase changes, or null if not available.</param>
    /// <param name="backendProgressUpdater">The backend progress updater for reporting transfer speed, or null if not available.</param>
    void OnFinish(IBasicResults result, Exception exception, IOperationProgressUpdater? progressUpdater, IBackendProgressUpdater? backendProgressUpdater);
}
