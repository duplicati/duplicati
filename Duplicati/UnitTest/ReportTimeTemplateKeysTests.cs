#nullable enable
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
using System.Globalization;
using System.Text.Json.Nodes;
using Duplicati.Library.Interface;
using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests the %BeginTime%, %EndTime% and %Duration% template keys of the report modules,
    /// expanded from the result when the operation finishes, in the subject and in the body
    /// </summary>
    [TestFixture]
    [Category("ReportHelper")]
    public class ReportTimeTemplateKeysTests
    {
        /// <summary>
        /// A minimal report module that records the messages it would have sent
        /// </summary>
        private sealed class RecordingReportModule : ReportHelper
        {
            public const string OPTION_SUBJECT = "test-subject";
            public const string OPTION_BODY = "test-body";
            public const string OPTION_RESULT_FORMAT = "test-result-format";

            public readonly List<(string Subject, string Body)> Sent = new();

            public override string Key => "test-report";
            public override string DisplayName => "Test report";
            public override string Description => "Report module used for testing";
            public override bool LoadAsDefault => false;
            public override IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            protected override string SubjectOptionName => OPTION_SUBJECT;
            protected override string BodyOptionName => OPTION_BODY;
            protected override string ActionLevelOptionName => "test-level";
            protected override string ActionOnAnyOperationOptionName => "test-any-operation";
            protected override string ActionOnOperationsOptionName => "test-operations";
            protected override string LogLevelOptionName => "test-log-level";
            protected override string LogFilterOptionName => "test-log-filter";
            protected override string LogLinesOptionName => "test-max-log-lines";
            protected override string ResultFormatOptionName => OPTION_RESULT_FORMAT;
            protected override string ExtraDataOptionName => "test-extra-parameters";

            protected override bool ConfigureModule(IDictionary<string, string> commandlineOptions) => true;
            protected override void SendMessage(string subject, string body) => Sent.Add((subject, body));
        }

        /// <summary>
        /// A finished backup that ran for a day, two hours, three minutes and four and a half seconds
        /// </summary>
        private sealed class FinishedResults : IBasicResults
        {
            public DateTime BeginTime => new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);
            public DateTime EndTime => BeginTime.Add(new TimeSpan(1, 2, 3, 4, 500));
            public TimeSpan Duration => EndTime - BeginTime;
            public IEnumerable<string> Errors => [];
            public IEnumerable<string> Warnings => [];
            public IEnumerable<string> Messages => [];
            public ParsedResultType ParsedResult => ParsedResultType.Success;
            public bool Interrupted => false;
        }

        /// <summary>
        /// Runs a backup through the module, the same way the controller does, and returns the one message sent
        /// </summary>
        private static (string Subject, string Body) Report(Dictionary<string, string> options, IBasicResults? result, Exception? exception = null)
        {
            options["test-level"] = "all";
            options["test-max-log-lines"] = "0";

            var module = new RecordingReportModule();
            module.Configure(options);

            var url = "file:///not-used";
            var paths = Array.Empty<string>();
            module.OnStart("Backup", ref url, ref paths);
            module.OnFinish(result!, exception!);

            Assert.That(module.Sent, Has.Count.EqualTo(1));
            return module.Sent[0];
        }

        /// <summary>
        /// The start or end time as the report shows it, in the local time of the machine
        /// </summary>
        private static string Local(DateTime time)
            => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        [Test]
        public void TimesAreExpandedInSubjectAndBody()
        {
            var result = new FinishedResults();
            var (subject, body) = Report(new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_SUBJECT] = "%PARSEDRESULT% in %Duration%",
                [RecordingReportModule.OPTION_BODY] = "begin=[%BeginTime%] end=[%endtime%] duration=[%DURATION%]",
            }, result);

            Assert.That(subject, Is.EqualTo("Success in 1.02:03:04"));
            Assert.That(body, Is.EqualTo($"begin=[{Local(result.BeginTime)}] end=[{Local(result.EndTime)}] duration=[1.02:03:04]"));
        }

        [Test]
        public void TimesAreTheSameInEveryCulture()
        {
            var result = new FinishedResults();
            var previous = CultureInfo.CurrentCulture;
            try
            {
                // Arabic (Saudi Arabia) uses another calendar and other digits by default
                CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
                var (_, body) = Report(new Dictionary<string, string>
                {
                    [RecordingReportModule.OPTION_BODY] = "%BeginTime% %Duration%",
                }, result);

                Assert.That(body, Is.EqualTo($"{Local(result.BeginTime)} 1.02:03:04"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void ResultWithoutTimesRemovesTheKeys()
        {
            // Without a result there are no times, and the keys are removed like any unknown key
            var (subject, body) = Report(new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_SUBJECT] = "%PARSEDRESULT% [%Duration%]",
                [RecordingReportModule.OPTION_BODY] = "begin=[%BeginTime%] end=[%EndTime%]",
            }, null, new Exception("Simulated failure"));

            Assert.That(subject, Is.EqualTo("Fatal []"));
            Assert.That(body, Is.EqualTo("begin=[] end=[]"));
        }

        [Test]
        public void AnOptionWithTheSameNameKeepsItsValue()
        {
            var (_, body) = Report(new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_BODY] = "duration=[%duration%] begin=[%begintime%]",
                ["duration"] = "from-option",
                ["test-extra-parameters"] = "{\"BeginTime\": \"from-extra\"}",
            }, new FinishedResults());

            Assert.That(body, Is.EqualTo("duration=[from-option] begin=[from-extra]"));
        }

        [Test]
        public void JsonReportCarriesTimesUsedInTheTemplate()
        {
            var result = new FinishedResults();
            var (subject, body) = Report(new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_SUBJECT] = "Took %Duration%",
                [RecordingReportModule.OPTION_BODY] = "%Duration% %BeginTime%",
                [RecordingReportModule.OPTION_RESULT_FORMAT] = "Json",
            }, result);

            Assert.That(subject, Is.EqualTo("Took 1.02:03:04"));
            var extra = JsonNode.Parse(body)?["Extra"];
            Assert.That(extra, Is.Not.Null, body);
            Assert.That(extra!["Duration"]?.GetValue<string>(), Is.EqualTo("1.02:03:04"));
            Assert.That(extra["BeginTime"]?.GetValue<string>(), Is.EqualTo(Local(result.BeginTime)));
            Assert.That(extra["EndTime"], Is.Null, "Only the keys used in the template are added");
        }
    }
}
