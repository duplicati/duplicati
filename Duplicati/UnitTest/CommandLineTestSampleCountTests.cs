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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The second argument of the test command is the number of samples. The Commandline page of the
/// UI fills the arguments with the backup sources, so test got a folder there and failed with a
/// FormatException from Convert.ToInt64 (issue #4082).
/// </summary>
public class CommandLineTestSampleCountTests : BasicSetupHelper
{
    private (int ExitCode, string Output) RunTest(params string[] arguments)
    {
        var args = new[] { "test", "file://" + TARGETFOLDER }
            .Concat(arguments)
            .Concat(TestOptions.Select(x => $"--{x.Key}={x.Value}"))
            .ToArray();

        using var output = new StringWriter();
        using var errors = new StringWriter();
        var exitCode = Duplicati.CommandLine.Program.RunCommandLine(output, errors, _ => { }, args);
        return (exitCode, output.ToString() + errors.ToString());
    }

    [Test]
    [Category("CommandLine")]
    public void ASampleCountThatIsNotANumberStopsWithAClearError()
    {
        Directory.CreateDirectory(TARGETFOLDER);
        var (exitCode, output) = RunTest(DATAFOLDER);

        Assert.That(exitCode, Is.EqualTo(200), output);
        Assert.That(output, Does.Contain(DATAFOLDER), "The error names the argument that was given");
        Assert.That(output, Does.Not.Contain("FormatException"), output);
    }

    [Test]
    [Category("CommandLine")]
    [TestCase("1")]
    [TestCase("all")]
    public async Task ASampleCountOrAllStillRunsTheTest(string samples)
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "f.txt"), "content");
        using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var (exitCode, output) = RunTest(samples);

        Assert.That(exitCode, Is.EqualTo(0), output);
    }
}
