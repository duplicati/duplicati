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
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.ResultSerialization;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The reduced report format is validated by the console ingress server and the activity
    /// monitor with these exact rules, so the client output must match them.
    /// </summary>
    [TestFixture]
    [Category("ReportModule")]
    public class ReducedReportFormatTests
    {
        /// <summary>
        /// The receivers' log line rule
        /// </summary>
        private static readonly Regex LogLineRule = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{2} - \[[^\]\s:]+\](?: \[ex:[^\]\s:]+\](?: \[st:[A-Za-z0-9_.+`<>|$]+(?:;[A-Za-z0-9_.+`<>|$]+){0,15}\])?){0,5}$");

        /// <summary>
        /// The receivers' exception rule
        /// </summary>
        private static readonly Regex ExceptionRule = new Regex(@"^[^\s:\[\]]+(?: \[helpid:[^\]\s:]+\])?(?: \[st:[A-Za-z0-9_.+`<>|$]+(?:;[A-Za-z0-9_.+`<>|$]+){0,15}\])?(?: \[ex:[^\]\s:]+\](?: \[st:[A-Za-z0-9_.+`<>|$]+(?:;[A-Za-z0-9_.+`<>|$]+){0,15}\])?){0,4}$");

        private const string SecretPath = "C:\\Patients\\John Doe\\MRI.pdf";

        [Test]
        public void LogLineCarriesOnlyTimestampFilterTagAndExceptionType()
        {
            var entry = new LogEntry("Excluding path due to permission denied: {0}", new object[] { SecretPath }, LogMessageType.Warning, "Duplicati.Library.Main.Operation.Backup.LogExceptionHelper", "PermissionDenied", new UnauthorizedAccessException($"Access to {SecretPath} is denied"));

            var line = ReducedReportFormat.FormatLogLine(entry);

            Assert.That(line, Does.Match(LogLineRule));
            Assert.That(line, Does.Contain("[Warning-Duplicati.Library.Main.Operation.Backup.LogExceptionHelper-PermissionDenied]"));
            Assert.That(line, Does.EndWith(" [ex:System.UnauthorizedAccessException]"));
            Assert.That(line, Does.Not.Contain("John Doe"));
            Assert.That(line, Does.Not.Contain("denied"));
        }

        [Test]
        public void LogLineWithoutExceptionHasNoSecondGroup()
        {
            var entry = new LogEntry("Message {0}", new object[] { SecretPath }, LogMessageType.Error, "Tag", "Id", null);
            var line = ReducedReportFormat.FormatLogLine(entry);
            Assert.That(line, Does.Match(LogLineRule));
            Assert.That(line, Does.Not.Contain("[ex:"));
        }

        [Test]
        public void GenericExceptionTypeNamesAreSanitized()
        {
            var entry = new LogEntry("m", new object[0], LogMessageType.Warning, "Tag", "Id", new KeyNotFoundException<string>());
            var line = ReducedReportFormat.FormatLogLine(entry);
            Assert.That(line, Does.Match(LogLineRule), line);
        }

        private sealed class KeyNotFoundException<T> : Exception
        {
        }

        [Test]
        public void ExceptionIsReducedToTypeNameAndHelpId()
        {
            var plain = ReducedReportFormat.FormatException(new System.IO.IOException($"Could not find file '{SecretPath}'"));
            var user = ReducedReportFormat.FormatException(new UserInformationException($"Source folder {SecretPath} is missing", "MissingSourceFolder"));

            Assert.That(plain, Is.EqualTo("System.IO.IOException"));
            Assert.That(user, Is.EqualTo("Duplicati.Library.Interface.UserInformationException [helpid:MissingSourceFolder]"));
            Assert.That(plain, Does.Match(ExceptionRule));
            Assert.That(user, Does.Match(ExceptionRule));
        }

        [TestCase("System.IO.IOException: Could not find file 'C:\\x y.txt'\n   at Foo.Bar()", "System.IO.IOException")]
        [TestCase("System.IO.IOException", "System.IO.IOException")]
        [TestCase("Some.Generic`1[[System.String, mscorlib]]: message", "Some.Generic`1")]
        [TestCase("", null)]
        [TestCase(null, null)]
        public void ExceptionTypeNameIsTakenFromTheStartOfTheText(string text, string expected)
        {
            Assert.That(ReducedReportFormat.ExceptionTypeNameFromText(text), Is.EqualTo(expected));
        }

        /// <summary>
        /// Throws from a named method, so the frame can be recognized
        /// </summary>
        private static void ThrowWithPathInMessage()
            => throw new System.IO.IOException($"Could not find file '{SecretPath}'");

        private static async System.Threading.Tasks.Task ThrowAsyncWithPathInMessage()
        {
            await System.Threading.Tasks.Task.Yield();
            throw new InvalidOperationException($"Backup of {SecretPath} failed");
        }

        private static Exception Catch(Action action)
        {
            try { action(); }
            catch (Exception ex) { return ex; }
            throw new InvalidOperationException("Nothing was thrown");
        }

        [Test]
        public void ThrownExceptionCarriesItsStackFramesWithoutTextOrFiles()
        {
            var exception = Catch(ThrowWithPathInMessage);
            var entry = new LogEntry("Failed on {0}", new object[] { SecretPath }, LogMessageType.Error, "Tag", "Id", exception);

            var line = ReducedReportFormat.FormatLogLine(entry);
            var text = ReducedReportFormat.FormatException(exception);

            Assert.That(line, Does.Match(LogLineRule), line);
            Assert.That(text, Does.Match(ExceptionRule), text);
            foreach (var value in new[] { line, text })
            {
                Assert.That(value, Does.Contain("[st:Duplicati.UnitTest.ReducedReportFormatTests.ThrowWithPathInMessage"), value);
                Assert.That(value, Does.Not.Contain("John Doe"));
                Assert.That(value, Does.Not.Contain("Could not find"));
                Assert.That(value, Does.Not.Contain(".cs"));
                Assert.That(value, Does.Not.Contain("("));
            }
        }

        [Test]
        public void AsyncMethodIsReportedByItsOwnName()
        {
            var exception = Catch(() => ThrowAsyncWithPathInMessage().GetAwaiter().GetResult());

            var frames = ReducedReportFormat.FormatStackFrames(exception);

            Assert.That(frames, Has.Some.EqualTo("Duplicati.UnitTest.ReducedReportFormatTests.ThrowAsyncWithPathInMessage"));
            Assert.That(frames, Has.None.Contains("MoveNext"));
            Assert.That(ReducedReportFormat.FormatException(exception), Does.Match(ExceptionRule));
        }

        [Test]
        public void InnerExceptionsAreReportedAsAChain()
        {
            var exception = Catch(() =>
            {
                try { ThrowWithPathInMessage(); }
                catch (Exception inner) { throw new InvalidOperationException($"Backup of {SecretPath} failed", inner); }
            });
            var entry = new LogEntry("m", new object[0], LogMessageType.Error, "Tag", "Id", exception);

            var line = ReducedReportFormat.FormatLogLine(entry);
            var text = ReducedReportFormat.FormatException(exception);

            Assert.That(line, Does.Match(LogLineRule), line);
            Assert.That(text, Does.Match(ExceptionRule), text);
            Assert.That(line, Does.Contain("[ex:System.InvalidOperationException]"));
            Assert.That(line, Does.Contain("[ex:System.IO.IOException]"));
            Assert.That(text, Does.StartWith("System.InvalidOperationException [st:"));
            Assert.That(text, Does.Contain(" [ex:System.IO.IOException] [st:Duplicati.UnitTest.ReducedReportFormatTests.ThrowWithPathInMessage"));
            Assert.That(line + text, Does.Not.Contain("John Doe"));
        }

        [Test]
        public void ChainAndFramesAreCapped()
        {
            Exception exception = new System.IO.IOException("innermost");
            for (var i = 0; i < 12; i++)
                exception = new InvalidOperationException($"level {i}", exception);

            var text = ReducedReportFormat.FormatException(exception);
            Assert.That(text, Does.Match(ExceptionRule), text);
            Assert.That(Regex.Matches(text, @"\[ex:").Count, Is.EqualTo(ReducedReportFormat.MAX_EXCEPTION_CHAIN - 1));

            static void Recurse(int depth)
            {
                if (depth == 0)
                    throw new InvalidOperationException("deep");
                Recurse(depth - 1);
            }

            var frames = ReducedReportFormat.FormatStackFrames(Catch(() => Recurse(40)));
            Assert.That(frames, Has.Count.EqualTo(ReducedReportFormat.MAX_STACK_FRAMES));
        }

        /// <summary>
        /// An exception that reports a stack trace of its own making
        /// </summary>
        private sealed class ExceptionWithCustomStackTrace : Exception
        {
            public override string StackTrace => $"   at Custom.Frame() in {SecretPath}:line 1";
        }

        [Test]
        public void StackTraceTextSuppliedByTheExceptionIsIgnored()
        {
            var exception = Catch(() => throw new ExceptionWithCustomStackTrace());

            var text = ReducedReportFormat.FormatException(exception);

            Assert.That(text, Does.Match(ExceptionRule), text);
            Assert.That(text, Does.Not.Contain("John Doe"));
            Assert.That(text, Does.Not.Contain("Custom.Frame"));
        }

        [Test]
        public void ExceptionThatWasNeverThrownHasNoStackGroup()
        {
            var text = ReducedReportFormat.FormatException(new System.IO.IOException("x"));
            Assert.That(text, Is.EqualTo("System.IO.IOException"));
        }

        [Test]
        public void ReducedResultViewKeepsNumbersDatesAndAllowedStringsOnly()
        {
            var result = new
            {
                MainOperation = "Backup",
                ParsedResult = "Warning",
                Version = "2.3.1.1 (2.3.1.1_canary)",
                BeginTime = new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc),
                Duration = TimeSpan.FromMinutes(5),
                ExaminedFiles = 1200L,
                Interrupted = false,
                Messages = new[] { $"2026-09-26 - [Information-Tag-Id]: Including {SecretPath}" },
                Warnings = new string[0],
                RestorePath = "C:\\Restore\\John Doe",
                BackendStatistics = new { KnownFileSize = 123L, LastErrorMessage = "Host ward7 unreachable" },
                TestResults = new
                {
                    MainOperation = "Test",
                    Verifications = new[] { new KeyValuePair<string, IEnumerable<KeyValuePair<string, string>>>("duplicati-b1.dblock.zip.aes", new[] { new KeyValuePair<string, string>("Error", SecretPath) }) }
                }
            };

            var reduced = ReducedResultView.Create(result) as Dictionary<string, object>;
            Assert.That(reduced, Is.Not.Null);

            Assert.That(reduced["MainOperation"], Is.EqualTo("Backup"));
            Assert.That(reduced["ParsedResult"], Is.EqualTo("Warning"));
            Assert.That(reduced["Version"], Is.EqualTo("2.3.1.1 (2.3.1.1_canary)"));
            Assert.That(reduced["ExaminedFiles"], Is.EqualTo(1200L));
            Assert.That(reduced["Interrupted"], Is.EqualTo(false));
            Assert.That(reduced.ContainsKey("RestorePath"), Is.False, "paths are dropped");
            Assert.That(reduced["Messages"], Is.Empty, "message lists are emptied");
            Assert.That(reduced["Warnings"], Is.Empty);
            Assert.That(ReducedResultView.IsDateOrDurationValue(reduced["BeginTime"] as string), Is.True, $"{reduced["BeginTime"]}");
            Assert.That(ReducedResultView.IsDateOrDurationValue(reduced["Duration"] as string), Is.True, $"{reduced["Duration"]}");

            var stats = reduced["BackendStatistics"] as Dictionary<string, object>;
            Assert.That(stats["KnownFileSize"], Is.EqualTo(123L));
            Assert.That(stats.ContainsKey("LastErrorMessage"), Is.False);

            var tests = reduced["TestResults"] as Dictionary<string, object>;
            Assert.That(tests["MainOperation"], Is.EqualTo("Test"));
            var verifications = tests["Verifications"] as List<object>;
            var verification = verifications.Single() as Dictionary<string, object>;
            Assert.That(verification.ContainsKey("Key"), Is.False, "volume names are dropped");
            var changes = verification["Value"] as List<object>;
            var change = changes.Single() as Dictionary<string, object>;
            Assert.That(change, Is.Empty, "both the status name and the path are dropped");

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(reduced);
            Assert.That(json, Does.Not.Contain("John Doe"));
            Assert.That(json, Does.Not.Contain("ward7"));
        }

        [Test]
        public void ReducedSerializerRendersReducedExceptionInEveryFormat()
        {
            var exception = new UserInformationException($"Source folder {SecretPath} is missing", "MissingSourceFolder");
            var loglines = new[] { "2026-09-26 10:00:00 +02 - [Warning-Tag-Id]" };
            var extra = new Dictionary<string, string> { ["report-mode"] = "reduced" };

            foreach (var format in new[] { ResultExportFormat.Json, ResultExportFormat.Duplicati })
            {
                var serializer = new ReducedResultFormatSerializer(ResultFormatSerializerProvider.GetSerializer(format));
                var body = serializer.Serialize(null, exception, loglines, extra);
                Assert.That(body, Does.Not.Contain("John Doe"), format.ToString());
                Assert.That(body, Does.Not.Contain("missing"), format.ToString());
                Assert.That(body, Does.Contain("UserInformationException"), format.ToString());
                Assert.That(body, Does.Contain("MissingSourceFolder"), format.ToString());
            }
        }

        [Test]
        public void ReducedJsonReportHasTheExpectedShape()
        {
            var result = new { MainOperation = "Backup", ParsedResult = "Success", Messages = new[] { "text" }, ExaminedFiles = 3 };
            var serializer = new ReducedResultFormatSerializer(new JsonFormatSerializer());
            var body = serializer.Serialize(result, null, new[] { "2026-09-26 10:00:00 +02 - [Warning-Tag-Id]" }, new Dictionary<string, string> { ["report-mode"] = "reduced" });

            var json = Newtonsoft.Json.Linq.JObject.Parse(body);
            Assert.That(json["Data"]["MainOperation"].ToString(), Is.EqualTo("Backup"));
            Assert.That(json["Data"]["Messages"], Is.Empty);
            Assert.That((int)json["Data"]["ExaminedFiles"], Is.EqualTo(3));
            Assert.That(json["Extra"]["report-mode"].ToString(), Is.EqualTo("reduced"));
            Assert.That(json["LogLines"].Single().ToString(), Does.Match(LogLineRule));
            Assert.That(json["Exception"].Type, Is.EqualTo(Newtonsoft.Json.Linq.JTokenType.Null));
        }
    }
}
