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
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests that the report modules only send reports for the configured operations:
    /// by default only backups, or the operations listed in the "operations" option,
    /// or every operation when the "any operation" option is set.
    /// </summary>
    [TestFixture]
    [Category("ReportHelper")]
    public class ReportHelperOperationsTests
    {
        /// <summary>
        /// A minimal report module that records the messages it would have sent
        /// </summary>
        private class RecordingReportModule : ReportHelper
        {
            public const string OPTION_ANY_OPERATION = "test-any-operation";
            public const string OPTION_OPERATIONS = "test-operations";

            public readonly List<(string Subject, string Body)> Sent = new();

            public override string Key => "test-report";
            public override string DisplayName => "Test report";
            public override string Description => "Report module used for testing";
            public override bool LoadAsDefault => false;
            public override IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            protected override string SubjectOptionName => "test-subject";
            protected override string BodyOptionName => "test-body";
            protected override string ActionLevelOptionName => "test-level";
            protected override string ActionOnAnyOperationOptionName => OPTION_ANY_OPERATION;
            protected override string ActionOnOperationsOptionName => OPTION_OPERATIONS;
            protected override string LogLevelOptionName => "test-log-level";
            protected override string LogFilterOptionName => "test-log-filter";
            protected override string LogLinesOptionName => "test-max-log-lines";
            protected override string ResultFormatOptionName => "test-result-format";
            protected override string ExtraDataOptionName => "test-extra-parameters";

            protected override bool ConfigureModule(IDictionary<string, string> commandlineOptions) => true;
            protected override void SendMessage(string subject, string body) => Sent.Add((subject, body));
        }

        /// <summary>
        /// Runs a module through a full operation and reports if a message was sent
        /// </summary>
        private static bool SendsReportFor(string operationName, Dictionary<string, string> options)
        {
            var module = new RecordingReportModule();
            module.Configure(options);

            var url = "file:///not-used";
            var paths = Array.Empty<string>();
            module.OnStart(operationName, ref url, ref paths);
            module.OnFinish(null, new Exception("Simulated failure"));

            return module.Sent.Count == 1;
        }

        [Test]
        public void DefaultOnlySendsForBackup()
        {
            var options = new Dictionary<string, string>();
            Assert.IsTrue(SendsReportFor("Backup", options));
            Assert.IsFalse(SendsReportFor("RestoreTest", options));
            Assert.IsFalse(SendsReportFor("Restore", options));
        }

        [Test]
        public void ListedOperationsSendCaseInsensitive()
        {
            var options = new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_OPERATIONS] = "backup,restoretest"
            };

            Assert.IsTrue(SendsReportFor("Backup", options));
            Assert.IsTrue(SendsReportFor("RestoreTest", options));
            Assert.IsTrue(SendsReportFor("RESTORETEST", options));
            Assert.IsFalse(SendsReportFor("Restore", options));
        }

        [Test]
        public void ListedOperationsReplaceTheDefault()
        {
            var options = new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_OPERATIONS] = "RestoreTest"
            };

            Assert.IsFalse(SendsReportFor("Backup", options));
            Assert.IsTrue(SendsReportFor("RestoreTest", options));
        }

        [Test]
        public void EmptyOperationListUsesTheDefault()
        {
            var options = new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_OPERATIONS] = "  "
            };

            Assert.IsTrue(SendsReportFor("Backup", options));
            Assert.IsFalse(SendsReportFor("RestoreTest", options));
        }

        [Test]
        public void AnyOperationOverridesTheList()
        {
            var options = new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_ANY_OPERATION] = "true",
                [RecordingReportModule.OPTION_OPERATIONS] = "RestoreTest"
            };

            Assert.IsTrue(SendsReportFor("Backup", options));
            Assert.IsTrue(SendsReportFor("RestoreTest", options));
            Assert.IsTrue(SendsReportFor("Restore", options));
        }

        [Test]
        public void OnlyUnknownOperationNamesUsesTheDefault()
        {
            var options = new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_OPERATIONS] = "not-an-operation"
            };

            Assert.IsTrue(SendsReportFor("Backup", options));
            Assert.IsFalse(SendsReportFor("RestoreTest", options));
        }

        [Test]
        public void UnknownOperationNameOnlySendsWithAnyOperation()
        {
            Assert.IsFalse(SendsReportFor("CustomOperation", new Dictionary<string, string>()));
            Assert.IsTrue(SendsReportFor("CustomOperation", new Dictionary<string, string>
            {
                [RecordingReportModule.OPTION_ANY_OPERATION] = "true"
            }));
        }

        [Test]
        public void BuiltinModulesExposeTheOperationsOption()
        {
            var expected = new Dictionary<IGenericModule, string>
            {
                [new SendHttpMessage()] = "send-http-operations",
                [new SendMail()] = "send-mail-operations",
                [new SendJabberMessage()] = "send-xmpp-operations",
                [new SendTelegramMessage()] = "send-telegram-operations",
            };

            foreach (var (module, optionName) in expected)
            {
                var argument = module.SupportedCommands.FirstOrDefault(x => string.Equals(x.Name, optionName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(argument, $"{module.Key} is missing the {optionName} option");
                Assert.AreEqual(nameof(OperationMode.Backup), argument!.DefaultValue, $"{optionName} should default to backups only");
                Assert.AreEqual(CommandLineArgument.ArgumentType.Flags, argument.Type, $"{optionName} should be a multi-select option");
                CollectionAssert.AreEquivalent(Enum.GetNames(typeof(OperationMode)), argument.ValidValues, $"{optionName} should list the operation names");
            }
        }
    }
}
