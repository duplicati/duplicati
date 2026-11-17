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

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// The timings for the action that is performed when the task queue has run empty
/// </summary>
/// <param name="SettleDelay">The time the queue must stay empty before the action is performed.
/// A finished backup can queue the backups that run after it, which happens just after the queue reports the task as completed.</param>
/// <param name="ScriptTimeout">The time a script gets to finish before the server stops waiting for it</param>
public sealed record QueueCompletionSettings(TimeSpan SettleDelay, TimeSpan ScriptTimeout)
{
    /// <summary>
    /// The timings used by the server
    /// </summary>
    public static readonly QueueCompletionSettings Default = new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
}
