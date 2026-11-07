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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The mail report was always sent as plain text, so HTML in the body showed as code (#7024)
/// </summary>
public class SendMailHtmlBodyTests
{
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
            if (command.StartsWith("EHLO") || command.StartsWith("HELO"))
                await writer.WriteLineAsync("250 test");
            else if (command.StartsWith("DATA"))
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
    /// Sends the report for a failed backup through a test server and returns the message
    /// </summary>
    private static Task<string> SendReportAsync(Dictionary<string, string> extraOptions)
        => SendReportAsync(extraOptions, new Exception("Failed on <b> & \"c\""));

    /// <summary>
    /// Sends the report for a backup that failed with the given error through a test server and returns the message
    /// </summary>
    private static async Task<string> SendReportAsync(Dictionary<string, string> extraOptions, Exception error)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = ReceiveOneMailAsync(listener);

            var options = new Dictionary<string, string>
            {
                ["send-mail-to"] = "someone@example.com",
                ["send-mail-from"] = "duplicati@example.com",
                ["send-mail-url"] = $"smtp://127.0.0.1:{port}",
                ["send-mail-level"] = "all",
                ["send-mail-subject"] = "Report <%PARSEDRESULT%> & more",
                ["send-mail-body"] = "<p>%OPERATIONNAME%</p><pre>%RESULT%</pre>"
            };
            foreach (var option in extraOptions)
                options[option.Key] = option.Value;

            var module = new SendMail();
            module.Configure(options);
            var url = "file:///not-used";
            var paths = Array.Empty<string>();
            module.OnStart("Backup", ref url, ref paths);
            module.OnFinish(null, error);

            return await server.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task TheBodyIsPlainTextByDefaultAsync()
    {
        var message = await SendReportAsync([]);

        Assert.That(message, Does.Contain("Content-Type: text/plain"));
        Assert.That(message, Does.Contain("Failed on <b> & \"c\""));
    }

    [Test]
    [Category("Targeted")]
    public async Task TheBodyIsSentAsHtmlWhenAskedForAsync()
    {
        var message = await SendReportAsync(new() { ["send-mail-body-html"] = "true" });

        Assert.That(message, Does.Contain("Content-Type: text/html"));
        Assert.That(message, Does.Contain("<p>Backup</p><pre>"));
    }

    /// <summary>
    /// The values put into the body are text, such as file names in the log, so they are
    /// escaped and cannot add markup to the message
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task TheValuesInAnHtmlBodyAreEscapedAsync()
    {
        var message = await SendReportAsync(new() { ["send-mail-body-html"] = "true" });

        Assert.That(message, Does.Contain("Failed on &lt;b&gt; &amp; &quot;c&quot;"));
        Assert.That(message, Does.Not.Contain("<b>"));
    }

    /// <summary>
    /// Paths were redacted in the whole body, which took the closing tags of the template for paths
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task TheTagsOfAnHtmlBodyAreKeptAsync()
    {
        var message = await SendReportAsync(new() { ["send-mail-body-html"] = "true" });

        Assert.That(message, Does.Contain("</pre>"));
        Assert.That(message, Does.Not.Contain("redacted"));
    }

    /// <summary>
    /// A reduced report redacts the whole body, which took the closing tags of the template for paths
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task TheTagsOfAnHtmlBodyAreKeptWithReducedReportingAsync()
    {
        var message = await SendReportAsync(new() { ["send-mail-body-html"] = "true", ["reduced-reporting"] = "true" });

        Assert.That(message, Does.Contain("<p>Backup</p><pre>"));
        Assert.That(message, Does.Contain("</pre>"));
        Assert.That(message, Does.Not.Contain("redacted"));
    }

    /// <summary>
    /// A reduced report carries no file names, also not the ones written in the template
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task PathsInTheTextOfAnHtmlBodyAreRedactedWithReducedReportingAsync()
    {
        var message = await SendReportAsync(new()
        {
            ["send-mail-body-html"] = "true",
            ["reduced-reporting"] = "true",
            ["send-mail-body"] = "<p>Backup of /home/someone/secret</p>"
        });

        Assert.That(message, Does.Not.Contain("secret"));
        Assert.That(message, Does.Contain("<p>Backup of "));
        Assert.That(message, Does.Contain("</p>"));
    }

    [Test]
    [Category("Targeted")]
    public async Task PathsInTheValuesOfAnHtmlBodyAreStillRedactedAsync()
    {
        var plain = await SendReportAsync([], new Exception("Cannot read /home/someone/secret/file.txt"));
        var html = await SendReportAsync(new() { ["send-mail-body-html"] = "true" }, new Exception("Cannot read /home/someone/secret/file.txt"));

        Assert.That(plain, Does.Not.Contain("secret"));
        Assert.That(html, Does.Not.Contain("secret"));
        Assert.That(html, Does.Contain("Cannot read"));
    }

    [Test]
    [Category("Targeted")]
    public async Task TheSubjectIsNotEscapedAsync()
    {
        var message = await SendReportAsync(new() { ["send-mail-body-html"] = "true" });

        Assert.That(message, Does.Contain("Subject: Report <Fatal> & more"));
    }
}
