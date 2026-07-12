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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Logging;
using Duplicati.Library.Main.Operation.Backup;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// https://github.com/duplicati/duplicati/issues/6426
    /// Warnings for known, expected path problems (permission denied, locked file, path not
    /// found, path too long) must not carry the exception, so their (large) stack traces do not
    /// bloat the log. Genuinely unexpected errors still keep the exception for debugging.
    /// </summary>
    public class Issue6426
    {
        private static List<LogEntry> CaptureWarnings(Exception ex, string callerId)
        {
            var captured = new List<LogEntry>();
            using (Log.StartScope(e => captured.Add(e)))
                LogExceptionHelper.LogCommonWarning(ex, "TestTag", callerId, "/some/path");
            return captured.Where(e => e.Level == LogMessageType.Warning).ToList();
        }

        [Test]
        [Category("Targeted")]
        public void KnownPathWarningsCarryNoException()
        {
            // These exception types are recognised as "known problems" on every platform.
            var cases = new (Exception Exception, string ExpectedId)[]
            {
                (new UnauthorizedAccessException("denied"), "PermissionDenied"),
                (new FileNotFoundException("missing"), "PathNotFound"),
                (new PathTooLongException("too long"), "PathTooLong"),
            };

            foreach (var c in cases)
            {
                var warnings = CaptureWarnings(c.Exception, "SomeCallerId");
                Assert.AreEqual(1, warnings.Count, "Expected exactly one warning for " + c.ExpectedId);
                Assert.AreEqual(c.ExpectedId, warnings[0].Id, "Unexpected warning id");
                Assert.IsNull(warnings[0].Exception,
                    c.ExpectedId + " is a known problem and must not attach the exception (no stack trace)");
            }
        }

        [Test]
        [Category("Targeted")]
        public void UnknownPathWarningKeepsExceptionForDebugging()
        {
            var ex = new InvalidOperationException("unexpected");
            var warnings = CaptureWarnings(ex, "CustomId");
            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual("CustomId", warnings[0].Id, "Unknown errors should use the caller-supplied id");
            Assert.AreSame(ex, warnings[0].Exception, "Unknown errors must keep the exception for debugging");
        }
    }
}
