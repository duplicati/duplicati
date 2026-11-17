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

using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// With --debug-output the command line lists its input options, but --include and --exclude
/// were not shown, as they are moved out of the options and into the filter (#2073).
/// </summary>
public class CommandLineDebugOutputFilterTests : BasicSetupHelper
{
    [SetUp]
    public void WriteSource()
        => File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "data");

    [Test]
    [Category("Targeted")]
    public void DebugOutputListsTheIncludeAndExcludeFilters()
    {
        var args = new[] { "backup", "file://" + TARGETFOLDER, DATAFOLDER }
            .Concat(TestOptions.Select(x => $"--{x.Key}={x.Value}"))
            .Concat(new[] { "--debug-output=true", "--include=*.txt", "--exclude=*.tmp" })
            .ToArray();

        using var output = new StringWriter();
        Duplicati.CommandLine.Program.RunCommandLine(output, output, _ => { }, args);
        var text = output.ToString();

        Assert.That(text, Does.Contain("Input filters: "), text);
        Assert.That(text, Does.Contain("\t+*.txt"), text);
        Assert.That(text, Does.Contain("\t-*.tmp"), text);
    }
}
