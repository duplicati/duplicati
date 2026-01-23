// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.Modules.Builtin;
using Duplicati.Library.Modules.Builtin.ResultSerialization;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class TemplateSerializerTests : BasicSetupHelper
    {
        [Test]
        [Category("Serialization")]
        public void TestGetSerializerGivenTemplateReturnsTemplateSerializer()
        {
            IResultFormatSerializer serializer = ResultFormatSerializerProvider.GetSerializer(ResultExportFormat.Template);
            Assert.That(serializer, Is.InstanceOf<TemplateFormatSerializer>());
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerFormat()
        {
            var serializer = new TemplateFormatSerializer();
            Assert.That(serializer.Format, Is.EqualTo(ResultExportFormat.Template));
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerWithSimpleTemplate()
        {
            var template = "Status: {{ParsedResult}}";
            var serializer = new TemplateFormatSerializer(template, false);

            var additional = new Dictionary<string, string>
            {
                { "ParsedResult", "Success" }
            };

            var result = serializer.Serialize(null, null, null, additional);
            Assert.That(result, Is.EqualTo("Status: Success"));
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerWithLogLines()
        {
            var template = "{{#each LogLines}}{{this}}\n{{/each}}";
            var serializer = new TemplateFormatSerializer(template, false);

            var logLines = new List<string> { "Line 1", "Line 2", "Line 3" };
            var result = serializer.Serialize(null, null, logLines, null);

            Assert.That(result, Does.Contain("Line 1"));
            Assert.That(result, Does.Contain("Line 2"));
            Assert.That(result, Does.Contain("Line 3"));
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerWithException()
        {
            var template = "{{#if Exception}}Error: {{Exception}}{{/if}}";
            var serializer = new TemplateFormatSerializer(template, false);

            var exception = new Exception("Test error message");
            var result = serializer.Serialize(null, exception, null, null);

            Assert.That(result, Does.Contain("Error:"));
            Assert.That(result, Does.Contain("Test error message"));
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerFormatSizeHelper()
        {
            var template = "Size: {{formatSize Data.Size}}";
            var serializer = new TemplateFormatSerializer(template, false);

            var result = serializer.Serialize(new { Size = 1536 }, null, null, null);
            Assert.That(result, Is.EqualTo("Size: 1.5 KB"));
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerFormatDurationHelper()
        {
            var template = "Duration: {{formatDuration 125}}";
            var serializer = new TemplateFormatSerializer(template, false);

            var result = serializer.Serialize(null, null, null, null);
            Assert.That(result, Is.EqualTo("Duration: 2m 5s"));
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerStatusClassHelper()
        {
            var template = "Class: {{statusClass ParsedResult}}";
            var serializer = new TemplateFormatSerializer(template, false);

            var additional = new Dictionary<string, string> { { "ParsedResult", "Warning" } };
            var result = serializer.Serialize(null, null, null, additional);

            Assert.That(result, Is.EqualTo("Class: warning"));
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerEqHelper()
        {
            var template = "{{#eq ParsedResult \"Success\"}}OK{{else}}FAIL{{/eq}}";
            var serializer = new TemplateFormatSerializer(template, false);

            var additional = new Dictionary<string, string> { { "ParsedResult", "Success" } };
            var result = serializer.Serialize(null, null, null, additional);

            Assert.That(result, Is.EqualTo("OK"));
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerFromFile()
        {
            var tempFile = Path.GetTempFileName() + ".hbs";
            try
            {
                File.WriteAllText(tempFile, "Custom template: {{OperationName}}");
                var serializer = TemplateFormatSerializer.FromFile(tempFile);

                var additional = new Dictionary<string, string> { { "OperationName", "Backup" } };
                var result = serializer.Serialize(null, null, null, additional);

                Assert.That(result, Is.EqualTo("Custom template: Backup"));
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerFromFileMissing()
        {
            Assert.Throws<FileNotFoundException>(() =>
                TemplateFormatSerializer.FromFile("/nonexistent/template.hbs"));
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateSerializerGetAvailableTemplates()
        {
            var templates = TemplateFormatSerializer.GetAvailableTemplates();
            Assert.That(templates, Is.Not.Null);
            // At minimum, we should have the default template
            Assert.That(templates, Does.Contain("default").Or.Empty);
        }

        [Test]
        [Category("Serialization")]
        public void TestResultFormatSerializerProviderWithTemplateName()
        {
            // Test that we can get a serializer with a template name
            var serializer = ResultFormatSerializerProvider.GetSerializer(ResultExportFormat.Template, "email");
            Assert.That(serializer, Is.InstanceOf<TemplateFormatSerializer>());
        }

        [Test]
        [Category("Serialization")]
        public void TestTemplateWithDataObject()
        {
            var template = "Files: {{Data.ExaminedFiles}}, Added: {{Data.AddedFiles}}";
            var serializer = new TemplateFormatSerializer(template, false);

            var data = new { ExaminedFiles = 100, AddedFiles = 5 };
            var result = serializer.Serialize(data, null, null, null);

            Assert.That(result, Is.EqualTo("Files: 100, Added: 5"));
        }
    }
}
