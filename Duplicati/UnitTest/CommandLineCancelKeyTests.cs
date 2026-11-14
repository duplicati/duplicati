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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Duplicati.CommandLine;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Ctrl+C killed the command line in the middle of an operation, so there was no clean way to
/// stop it (#2839). It now stops the operation, aborts it on the second press, and lets the
/// process end on the third.
/// </summary>
[NonParallelizable]
public class CommandLineCancelKeyTests : BasicSetupHelper
{
    /// <summary>
    /// The exit code of a command that stopped with an error
    /// </summary>
    private const int ErrorExitCode = 100;

    private string[] BackupArgs()
        => new[] { "backup", "file://" + TARGETFOLDER, DATAFOLDER }
            .Concat(TestOptions.Select(x => $"--{x.Key}={x.Value}"))
            .ToArray();

    /// <summary>
    /// Builds the arguments that the runtime passes to <see cref="Console.CancelKeyPress"/>
    /// </summary>
    private static ConsoleCancelEventArgs NewCancelEventArgs()
        => (ConsoleCancelEventArgs)Activator.CreateInstance(
            typeof(ConsoleCancelEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new object[] { ConsoleSpecialKey.ControlC },
            null)!;

    [SetUp]
    public void WriteSource()
    {
        var rng = new Random(42);
        var data = new byte[64 * 1024];
        for (var i = 0; i < 20; i++)
        {
            rng.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, $"file{i}"), data);
        }
    }

    /// <summary>
    /// Runs a backup from the command line and presses Ctrl+C the given number of times while
    /// it runs
    /// </summary>
    private (int ExitCode, string Output, List<bool> Cancelled) RunBackupWithPresses(int presses)
    {
        using var output = new StringWriter();
        var handler = new CancelKeyHandler(output);
        var cancelled = new List<bool>();

        var exitCode = Duplicati.CommandLine.Program.RunCommandLine(output, output, c =>
        {
            handler.Track(c);
            for (var i = 0; i < presses; i++)
            {
                var e = NewCancelEventArgs();
                handler.OnCancelKeyPress(null, e);
                cancelled.Add(e.Cancel);
            }
        }, BackupArgs());

        return (exitCode, output.ToString(), cancelled);
    }

    [Test]
    [Category("Targeted")]
    public void TheFirstCtrlCStopsTheBackupAndKeepsTheProcess()
    {
        var (_, output, cancelled) = RunBackupWithPresses(1);

        Assert.That(cancelled, Is.EqualTo(new[] { true }), "The first press must not end the process");
        Assert.That(output, Does.Contain("Backup was stopped before it completed"), output);
    }

    [Test]
    [Category("Targeted")]
    public void TheSecondCtrlCAbortsTheBackup()
    {
        var (exitCode, output, cancelled) = RunBackupWithPresses(2);

        Assert.That(cancelled, Is.EqualTo(new[] { true, true }), "The second press must not end the process");
        Assert.That(exitCode, Is.EqualTo(ErrorExitCode), output);
        Assert.That(Directory.EnumerateFiles(TARGETFOLDER, "*.dlist.*"), Is.Empty, output);
    }

    [Test]
    [Category("Targeted")]
    public void TheThirdCtrlCEndsTheProcess()
    {
        var (_, _, cancelled) = RunBackupWithPresses(3);

        Assert.That(cancelled, Is.EqualTo(new[] { true, true, false }), "The third press must let the process end");
    }

    [Test]
    [Category("Targeted")]
    public void CtrlCBeforeAnyOperationEndsTheProcess()
    {
        using var output = new StringWriter();
        var handler = new CancelKeyHandler(output);
        var e = NewCancelEventArgs();

        handler.OnCancelKeyPress(null, e);

        Assert.That(e.Cancel, Is.False, "With no operation to stop, Ctrl+C must end the process as before");
    }
}
