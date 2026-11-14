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
/// The command line went on with a warning when an option it typed was not known, such as a
/// misspelled option, which then did nothing (#7123). Typed options stop it now; options from a
/// parameters file, as in a saved job, still only warn.
/// </summary>
public class CommandLineUnsupportedOptionTests : BasicSetupHelper
{
    /// <summary>
    /// The exit code of a backup that finished without warnings
    /// </summary>
    private const int SuccessExitCode = 0;

    /// <summary>
    /// The exit code of a backup that finished with warnings
    /// </summary>
    private const int WarningExitCode = 2;

    /// <summary>
    /// The exit code of a command that stopped with an error
    /// </summary>
    private const int ErrorExitCode = 100;

    private string[] BackupArgs(params string[] extra)
        => new[] { "backup", "file://" + TARGETFOLDER, DATAFOLDER }
            .Concat(TestOptions.Select(x => $"--{x.Key}={x.Value}"))
            .Concat(extra)
            .ToArray();

    private static (int ExitCode, string Output) Run(string[] args, bool rejectUnsupportedOptions)
    {
        using var output = new StringWriter();
        var exitCode = Duplicati.CommandLine.Program.RunCommandLine(output, output, _ => { }, args, rejectUnsupportedOptions);
        return (exitCode, output.ToString());
    }

    private bool BackedUp()
        => Directory.EnumerateFiles(TARGETFOLDER, "*.dlist.*").Any();

    [SetUp]
    public void WriteSource()
        => File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "data");

    [Test]
    [Category("Targeted")]
    public void ATypedUnsupportedOptionStopsTheCommandLine()
    {
        var (exitCode, output) = Run(BackupArgs("--bla=path/"), true);

        Assert.That(exitCode, Is.EqualTo(ErrorExitCode), output);
        Assert.That(output, Does.Contain("is not supported").And.Not.Contain("will be ignored"));
        Assert.That(output, Does.Contain("--bla"));
        Assert.That(BackedUp(), Is.False, "The backup ran");
    }

    [Test]
    [Category("Targeted")]
    public void ATypedOptionOfAModuleThatIsNotLoadedStopsTheCommandLine()
    {
        var (exitCode, output) = Run(BackupArgs("--disable-module=sendmail", "--send-mail-to=someone@example.com"), true);

        Assert.That(exitCode, Is.EqualTo(ErrorExitCode), output);
        Assert.That(output, Does.Contain("is not supported").And.Not.Contain("will be ignored"));
        Assert.That(output, Does.Contain("--send-mail-to"));
        Assert.That(BackedUp(), Is.False, "The backup ran");
    }

    [Test]
    [Category("Targeted")]
    public void AnUnsupportedOptionInAParametersFileOnlyWarns()
    {
        var parameters = Path.Combine(BASEFOLDER, "parameters.txt");
        File.WriteAllText(parameters, "--bla=path/\n");

        var (exitCode, output) = Run(BackupArgs("--parameters-file=" + parameters), true);

        Assert.That(exitCode, Is.EqualTo(WarningExitCode), output);
        Assert.That(BackedUp(), Is.True, "The backup did not run");
    }

    /// <summary>
    /// The options of the command line tool are handled by the tool, so the controller does not
    /// report them as unsupported either, which made the backup end with warnings
    /// </summary>
    [Test]
    [Category("Targeted")]
    public void TheOptionsOfTheCommandLineToolAreAccepted([Values] bool rejectUnsupportedOptions)
    {
        var (exitCode, output) = Run(BackupArgs("--quiet-console=true"), rejectUnsupportedOptions);

        Assert.That(exitCode, Is.EqualTo(SuccessExitCode), output);
        Assert.That(BackedUp(), Is.True, "The backup did not run");
    }

    /// <summary>
    /// Without any other option, the backup has no warnings, so the warnings above come from the options
    /// </summary>
    [Test]
    [Category("Targeted")]
    public void ABackupWithKnownOptionsHasNoWarnings()
    {
        var (exitCode, output) = Run(BackupArgs(), true);

        Assert.That(exitCode, Is.EqualTo(SuccessExitCode), output);
    }

    /// <summary>
    /// The web UI runs the command line with the options of the saved job, which have to keep working
    /// </summary>
    [Test]
    [Category("Targeted")]
    public void WithoutTheSwitchAnUnsupportedOptionOnlyWarns()
    {
        var (exitCode, output) = Run(BackupArgs("--bla=path/"), false);

        Assert.That(exitCode, Is.EqualTo(WarningExitCode), output);
        Assert.That(BackedUp(), Is.True, "The backup did not run");
    }
}
