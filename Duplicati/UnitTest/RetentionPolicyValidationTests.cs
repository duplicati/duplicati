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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A backup with a retention policy that cannot be used fails before it starts. The error has to
/// say which option is wrong and why, as the bare parser message ("Unparsed data: ;") did not.
/// </summary>
public class RetentionPolicyValidationTests : BasicSetupHelper
{
    // The trailing semicolon is the value from issue #7425 ("Unparsed data: ;", which is translated);
    // the second value has an interval (48 months) bigger than the timeframe it is in (2 years).
    [TestCase("2W:U,2M:60D,4M:16W,2Y:48M;", null)]
    [TestCase("2W:U,2M:60D,4M:16W,2Y:48M", "IntervalCannotBeBiggerThanTimeFrame")]
    [Category("Utility")]
    public void ABackupWithAnInvalidRetentionPolicyNamesTheOptionAndTheCause(string retentionPolicy, string? causeHelpId)
    {
        Directory.CreateDirectory(TARGETFOLDER);
        var options = new Dictionary<string, string>(TestOptions) { ["retention-policy"] = retentionPolicy };

        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var ex = Assert.ThrowsAsync<UserInformationException>(() => c.BackupAsync(new[] { DATAFOLDER }));

        Assert.That(ex!.HelpID, Is.EqualTo("RetentionPolicyParseError"));
        Assert.That(ex.Message, Does.Contain("--retention-policy"), "The error should name the option");
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.Message, Does.Contain(ex.InnerException!.Message), "The error should say what is wrong with the value");
        if (causeHelpId != null)
            Assert.That((ex.InnerException as UserInformationException)?.HelpID, Is.EqualTo(causeHelpId));
    }
}
