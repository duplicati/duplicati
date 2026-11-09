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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Paths were redacted in the whole report after the template was expanded. The send-http module
/// expands the template itself and sends that text, so its reports were not redacted at all, and
/// the other modules also redacted paths that the user wrote into the template.
/// </summary>
[TestFixture]
[Category("ReportModule")]
public class ReportPathRedactionTests
{
    private const string SecretPath = "/home/someone/secret";

    /// <summary>
    /// Sends the report of a backup of <see cref="SecretPath"/> through the send-http module and
    /// returns the message parameter of the form a local receiver got
    /// </summary>
    private static async Task<string> ReceiveHttpFormReportAsync(Dictionary<string, string> options)
        => Uri.UnescapeDataString((await ReceiveHttpReportAsync(options, new Exception("Failed"))).Replace('+', ' '));

    /// <summary>
    /// Sends the report of a backup of <see cref="SecretPath"/> as JSON through the send-http module
    /// and returns the document a local receiver got
    /// </summary>
    private static async Task<JsonNode> ReceiveHttpJsonReportAsync(Dictionary<string, string> options, Exception error)
    {
        options["send-http-result-output-format"] = "Json";
        var body = await ReceiveHttpReportAsync(options, error);
        return JsonNode.Parse(body) ?? throw new AssertionException("The report is not JSON: " + body);
    }

    /// <summary>
    /// Sends the report of a backup of <see cref="SecretPath"/> through the send-http module and
    /// returns the body a local receiver got
    /// </summary>
    private static async Task<string> ReceiveHttpReportAsync(Dictionary<string, string> options, Exception error)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var prefix = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var received = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream);
            var form = await reader.ReadToEndAsync();
            context.Response.StatusCode = 200;
            context.Response.Close();
            return form;
        });

        options["send-http-url"] = prefix;
        options["send-http-retries"] = "0";
        options["send-http-level"] = "all";

        var module = new SendHttpMessage();
        module.Configure(options);
        var url = "file:///not-used";
        var paths = new[] { SecretPath };
        module.OnStart("Backup", ref url, ref paths);
        module.OnFinish(null, error);

        Assert.That(await Task.WhenAny(received, Task.Delay(TimeSpan.FromSeconds(30))), Is.SameAs(received), "The receiver got no report");
        return await received;
    }

    [Test]
    public async Task APathPutIntoAnHttpReportIsRedactedAsync()
    {
        var message = await ReceiveHttpFormReportAsync(new() { ["send-http-message"] = "Source: %LOCALPATH%" });

        Assert.That(message, Does.Contain("Source: -redacted-"));
        Assert.That(message, Does.Not.Contain("secret"));
    }

    [Test]
    public async Task APathInAJsonHttpReportIsRedactedAsync()
    {
        var report = await ReceiveHttpJsonReportAsync(new(), new Exception($"Cannot read {SecretPath}/file.txt"));

        Assert.That(report["Exception"]?.GetValue<string>(), Does.Contain("Cannot read -redacted-"));
        Assert.That(report.ToJsonString(), Does.Not.Contain("secret"));
    }

    /// <summary>
    /// The paths were found in the compact JSON text, where a path also took the closing quote and
    /// the properties after it up to the next space, so those properties were lost
    /// </summary>
    [Test]
    public async Task RedactingAJsonReportKeepsTheOtherValuesAsync()
    {
        var report = await ReceiveHttpJsonReportAsync(new()
        {
            ["backup-name"] = "/srv/backup",
            ["machine-name"] = "Office machine",
            ["machine-id"] = "machine-1"
        }, new Exception("Failed"));

        var extra = report["Extra"];
        Assert.That(extra?["backup-name"]?.GetValue<string>(), Is.EqualTo("-redacted-"));
        Assert.That(extra?["machine-name"]?.GetValue<string>(), Is.EqualTo("Office machine"));
        Assert.That(extra?["machine-id"]?.GetValue<string>(), Is.EqualTo("machine-1"));
    }

    /// <summary>
    /// A reduced JSON report keeps the full set of keys, while a path in a value is redacted
    /// </summary>
    [Test]
    public async Task AReducedJsonReportKeepsItsKeysAsync()
    {
        var report = await ReceiveHttpJsonReportAsync(new()
        {
            [Duplicati.Library.Logging.ReducedReportFormat.OPTION_REDUCED_REPORTING] = "true",
            ["backup-name"] = "/srv/backup",
            ["machine-name"] = "Office machine"
        }, new Exception($"Cannot read {SecretPath}/file.txt"));

        var extra = report["Extra"]!.AsObject();
        Assert.That(extra.Select(x => x.Key), Is.EquivalentTo(ReportHelper.EXTRA_TEMPLATE_KEYS.Append(Duplicati.Library.Logging.ReducedReportFormat.REPORT_MODE_KEY)));
        Assert.That(extra["backup-name"]?.GetValue<string>(), Is.EqualTo("-redacted-"));
        Assert.That(extra["machine-name"]?.GetValue<string>(), Is.EqualTo("Office machine"));
        Assert.That(report.ToJsonString(), Does.Not.Contain("secret"));
    }

    /// <summary>
    /// A reduced report carries no file names, so a path written in the template is also redacted
    /// </summary>
    [Test]
    public async Task AReducedReportRedactsThePathsInTheTemplateAsync()
    {
        var message = await ReceiveHttpFormReportAsync(new()
        {
            [Duplicati.Library.Logging.ReducedReportFormat.OPTION_REDUCED_REPORTING] = "true",
            ["send-http-message"] = "Backup of /srv/data from [%LOCALPATH%]: %PARSEDRESULT%"
        });

        Assert.That(message, Does.Contain("Backup of -redacted- from []: Fatal"));
        Assert.That(message, Does.Not.Contain("/srv/data"));
        Assert.That(message, Does.Not.Contain("secret"));
    }

    /// <summary>
    /// A body that names a template file which cannot be read was sent as it was, so the path of the
    /// file was the report; the default body is sent instead
    /// </summary>
    [Test]
    public async Task ABodyFileThatCannotBeReadIsNotSentAsync()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "report-template.txt");
        var message = await ReceiveHttpFormReportAsync(new() { ["send-http-message"] = missing });

        Assert.That(message, Does.StartWith("message=Duplicati Backup report for"));
        Assert.That(message, Does.Not.Contain("report-template"));
    }

    [Test]
    public async Task PathsAreKeptWhenAllowedAsync()
    {
        var message = await ReceiveHttpFormReportAsync(new()
        {
            ["send-http-message"] = "Source: %LOCALPATH%",
            ["allow-paths-in-log-messages"] = "true"
        });

        Assert.That(message, Does.Contain("Source: " + SecretPath));
    }

    /// <summary>
    /// Accepts one connection as a plain SMTP server and returns the message it was given
    /// </summary>
    private static async Task<string> ReceiveOneMailAsync(TcpListener listener)
    {
        using var tcp = await listener.AcceptTcpClientAsync();
        using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        var message = new StringBuilder();
        await writer.WriteLineAsync("220 test ESMTP");
        while (await reader.ReadLineAsync() is { } line)
        {
            var command = line.ToUpperInvariant();
            if (command.StartsWith("DATA"))
            {
                await writer.WriteLineAsync("354 go ahead");
                while (await reader.ReadLineAsync() is { } data && data != ".")
                    message.Append(data).Append('\n');
                await writer.WriteLineAsync("250 queued");
            }
            else if (command.StartsWith("QUIT"))
            {
                await writer.WriteLineAsync("221 bye");
                break;
            }
            else
                await writer.WriteLineAsync("250 ok");
        }

        return message.ToString();
    }

    /// <summary>
    /// The send-mail module redacted the whole message, so a path that the user wrote into the
    /// subject or the body was replaced, while the paths in the values must still be
    /// </summary>
    [Test]
    public async Task APathWrittenInAMailTemplateIsKeptAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var server = ReceiveOneMailAsync(listener);
            var module = new SendMail();
            module.Configure(new Dictionary<string, string>
            {
                ["send-mail-to"] = "someone@example.com",
                ["send-mail-from"] = "duplicati@example.com",
                ["send-mail-url"] = $"smtp://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}",
                ["send-mail-level"] = "all",
                ["send-mail-subject"] = "Backup of /srv/data: %PARSEDRESULT%",
                ["send-mail-body"] = "Backup of /srv/data\nSource: %LOCALPATH%"
            });
            var url = "file:///not-used";
            var paths = new[] { SecretPath };
            module.OnStart("Backup", ref url, ref paths);
            module.OnFinish(null, new Exception("Failed"));

            var message = await server.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.That(message, Does.Contain("Subject: Backup of /srv/data: Fatal"));
            Assert.That(message, Does.Contain("Backup of /srv/data\nSource: -redacted-"));
            Assert.That(message, Does.Not.Contain("secret"));
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// The text of the template is written by the user, so a path in it is not redacted
    /// </summary>
    [Test]
    public async Task APathWrittenInTheTemplateIsKeptAsync()
    {
        var message = await ReceiveHttpFormReportAsync(new() { ["send-http-message"] = "Backup of /srv/data: %PARSEDRESULT%" });

        Assert.That(message, Does.Contain("Backup of /srv/data: Fatal"));
    }
}
