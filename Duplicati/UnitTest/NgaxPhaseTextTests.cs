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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Main;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The old web UI (ngax) shows the text for the phase the server reports, and the name of the
/// phase itself when it has no text for it. A key that is not a phase name is never shown.
/// </summary>
[TestFixture]
public class NgaxPhaseTextTests
{
    [Test]
    public void EveryPhaseTextKeyIsAPhaseName()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "webroot", "ngax", "scripts", "services", "ServerStatus.js"));
        var table = Regex.Match(script, @"progress_state_text\s*=\s*\{(?<body>.*?)\};", RegexOptions.Singleline);
        Assert.That(table.Success, Is.True, "The phase texts were not found");

        var keys = Regex.Matches(table.Groups["body"].Value, @"^\s*'(?<key>[^']*)'\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups["key"].Value)
            .ToList();
        Assert.That(keys, Is.Not.Empty);

        var phases = Enum.GetNames<OperationPhase>();
        Assert.That(keys.Where(key => !phases.Contains(key)), Is.Empty, "These texts are for phases that do not exist");
    }
}
