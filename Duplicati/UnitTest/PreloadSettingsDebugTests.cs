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

using Duplicati.Library.AutoUpdater;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// DUPLICATI_PRELOAD_SETTINGS_DEBUG=0 turned the preload debug output on, as any value that was
/// set did (issue #6308). A value that means false now turns it off; other values still turn it on.
/// </summary>
public class PreloadSettingsDebugTests
{
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase(" ", false)]
    [TestCase("0", false)]
    [TestCase("false", false)]
    [TestCase("off", false)]
    [TestCase("no", false)]
    [TestCase("1", true)]
    [TestCase("true", true)]
    [TestCase("yes", true)]
    [TestCase("debug", true)]
    public void TheDebugVariableIsReadAsABoolean(string? value, bool expected)
        => Assert.That(PreloadSettingsLoader.IsDebugEnabled(value), Is.EqualTo(expected));
}
