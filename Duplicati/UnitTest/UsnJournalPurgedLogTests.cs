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
using System.Linq;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation;
using Duplicati.Library.Snapshots.USN;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The default USN journal on a system drive holds only a few hours of changes, so with a
/// daily backup the entries since the previous backup are gone and every backup scans the
/// volume in full, even with the USN policy set to required (issue #4175). That was logged as
/// information only. Reading the journal needs an elevated process, so the logging is called
/// with the exception the journal throws.
/// </summary>
public class UsnJournalPurgedLogTests
{
    private static List<LogEntry> Capture(System.Exception? exception, Options.OptimizationStrategy strategy)
    {
        var messages = new List<LogEntry>();
        using (Log.StartIsolatingScope(true))
        using (Log.StartScope(messages.Add))
            BackupHandler.LogUsnFullScan(@"C:\", exception, strategy);
        return messages;
    }

    private static UsnJournalPurgedException Purged()
        => new UsnJournalPurgedException("USN journal entries were purged since last scan. The USN journal of C: holds at most 32 MB", new System.ComponentModel.Win32Exception(1181));

    [Test]
    [Category("USN")]
    [TestCase(Options.OptimizationStrategy.On)]
    [TestCase(Options.OptimizationStrategy.Required)]
    public void APurgedJournalIsAWarningWhenTheJournalWasAskedFor(Options.OptimizationStrategy strategy)
    {
        var messages = Capture(Purged(), strategy);

        var entry = messages.Single();
        Assert.That(entry.Level, Is.EqualTo(LogMessageType.Warning));
        Assert.That(entry.Id, Is.EqualTo("UsnJournalPurged"));
        Assert.That(entry.FormattedMessage, Does.Contain("holds at most 32 MB"));
    }

    [Test]
    [Category("USN")]
    public void APurgedJournalStaysInformationWithTheAutoPolicy()
    {
        var entry = Capture(Purged(), Options.OptimizationStrategy.Auto).Single();

        Assert.That(entry.Level, Is.EqualTo(LogMessageType.Information));
        Assert.That(entry.Id, Is.EqualTo("SkipUsnForVolume"));
    }

    [Test]
    [Category("USN")]
    public void OtherReasonsForAFullScanStayInformation()
    {
        var entry = Capture(new UsnJournalSoftFailureException("Previous backup did not record USN journal info"), Options.OptimizationStrategy.Required).Single();

        Assert.That(entry.Level, Is.EqualTo(LogMessageType.Information));
        Assert.That(entry.Id, Is.EqualTo("SkipUsnForVolume"));
    }
}
