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

namespace Duplicati.Library.Main
{
    /// <summary>
    /// The interval at which progress is reported. Everything that reports or reads progress
    /// periodically uses this value, so it can be changed in one place if it interferes too much
    /// with the operations:
    /// <list type="bullet">
    /// <item>how often a file being read sends a progress update while a backup runs</item>
    /// <item>how often the server reads progress from an operation running in another process</item>
    /// <item>how often the server sends progress and system status to subscribed clients;
    /// clients that poll use the interval sent with each system status snapshot</item>
    /// </list>
    /// </summary>
    public static class ProgressReporting
    {
        /// <summary>
        /// The interval between progress reports
        /// </summary>
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    }
}
