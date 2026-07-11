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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class RetentionPolicyParsingTests
    {
        [Test]
        [Category("Utility")]
        public void ValidTimeframeIntervalPairIsParsed()
        {
            var value = Options.RetentionPolicyValue.CreateFromString("7D:1D");
            Assert.That(value.Timeframe, Is.EqualTo(TimeSpan.FromDays(7)));
            Assert.That(value.Interval, Is.EqualTo(TimeSpan.FromDays(1)));
        }

        [Test]
        [Category("Utility")]
        public void UnlimitedKeywordsAreParsed()
        {
            var unlimitedTimeframe = Options.RetentionPolicyValue.CreateFromString("U:1D");
            Assert.That(unlimitedTimeframe.IsUnlimtedTimeframe(), Is.True);

            var keepAll = Options.RetentionPolicyValue.CreateFromString("7D:U");
            Assert.That(keepAll.IsKeepAllVersions(), Is.True);
        }

        [Test]
        [Category("Utility")]
        public void MissingColonThrowsUserInformationException()
        {
            // Before the fix this threw an IndexOutOfRangeException (periodInterval[1]).
            Assert.Throws<UserInformationException>(() => Options.RetentionPolicyValue.CreateFromString("7D"));
        }

        [Test]
        [Category("Utility")]
        public void TooManySegmentsThrowsUserInformationException()
        {
            // Before the fix the extra segment was silently discarded, applying a
            // different retention policy than the user specified.
            Assert.Throws<UserInformationException>(() => Options.RetentionPolicyValue.CreateFromString("7D:1D:5h"));
        }
    }
}
