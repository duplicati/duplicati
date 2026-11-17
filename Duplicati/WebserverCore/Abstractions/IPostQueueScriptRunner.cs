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

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Runs the script from the "post-queue-processing-script" server setting,
/// which the user can choose to run when the task queue has run empty
/// </summary>
public interface IPostQueueScriptRunner
{
    /// <summary>
    /// Checks if a script is set and the file exists
    /// </summary>
    /// <returns>True if the script can be chosen</returns>
    bool CanRun();

    /// <summary>
    /// Runs the script and waits for it to finish
    /// </summary>
    /// <param name="cancellationToken">The token that cancels waiting for the script</param>
    /// <returns>A task that completes when the script has finished</returns>
    Task RunAsync(CancellationToken cancellationToken);
}
