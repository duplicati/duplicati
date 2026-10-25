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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Duplicati.Library.Logging;
using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Checks that the HTTP report module sends to every configured target
/// </summary>
[Category("ReportModule")]
public class SendHttpMessageTargetsTests
{
    /// <summary>
    /// Records the path of every request, and answers requests for /fail with 500
    /// </summary>
    private sealed class FakeReportServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public int Port { get; }
        public ConcurrentQueue<string> RequestPaths { get; } = new();

        public FakeReportServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        public string Url(string path) => $"http://localhost:{Port}/{path}";

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (!_listener.IsListening)
                {
                    return;
                }

                var path = context.Request.Url?.AbsolutePath ?? "";
                RequestPaths.Enqueue(path);
                context.Response.StatusCode = path == "/fail" ? 500 : 200;
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    private sealed class LogSink : ILogDestination
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public void WriteMessage(LogEntry entry)
        {
            Entries.Enqueue(entry);
        }
    }

    /// <summary>
    /// Runs the module through a backup with the given targets and returns the warnings it logged
    /// </summary>
    private static List<LogEntry> RunBackupReport(string? formUrls, string? jsonUrls)
    {
        var options = new Dictionary<string, string>
        {
            ["send-http-retries"] = "1",
            ["send-http-retry-delay"] = "0s",
        };
        if (formUrls != null)
            options["send-http-form-urls"] = formUrls;
        if (jsonUrls != null)
            options["send-http-json-urls"] = jsonUrls;

        var sink = new LogSink();
        using (Log.StartScope(sink, LogMessageType.Warning))
        {
            var module = new SendHttpMessage();
            module.Configure(options);

            var url = "file:///not-used";
            var paths = Array.Empty<string>();
            module.OnStart("Backup", ref url, ref paths);
            module.OnFinish(null, new Exception("Simulated failure"));
        }

        return sink.Entries.ToList();
    }

    [Test]
    public void FailingTargetDoesNotStopTheNextOne()
    {
        using var server = new FakeReportServer();

        var warnings = RunBackupReport(server.Url("fail"), server.Url("ok"));

        Assert.That(server.RequestPaths.ToArray(), Is.EqualTo(new[] { "/fail", "/ok" }),
            "The report was not sent to the target after the failing one");
        Assert.That(warnings.Count(x => x.Id == "ReportSubmitError"), Is.EqualTo(1),
            "The failure of the first target was not reported");
    }

    [Test]
    public void EveryFailingTargetIsTried()
    {
        using var server = new FakeReportServer();

        var warnings = RunBackupReport(server.Url("fail") + ";" + server.Url("fail?second"), null);

        Assert.That(server.RequestPaths.ToArray(), Is.EqualTo(new[] { "/fail", "/fail" }));
        Assert.That(warnings.Count(x => x.Id == "HttpResponseError"), Is.EqualTo(2));
        Assert.That(warnings.Count(x => x.Id == "ReportSubmitError"), Is.EqualTo(1));
    }

    [Test]
    public void SingleFailingTargetIsReportedOnce()
    {
        using var server = new FakeReportServer();

        var warnings = RunBackupReport(server.Url("fail"), null);

        Assert.That(server.RequestPaths.ToArray(), Is.EqualTo(new[] { "/fail" }));
        Assert.That(warnings.Count(x => x.Id == "HttpResponseError"), Is.EqualTo(1));
        var submitError = warnings.Single(x => x.Id == "ReportSubmitError");
        Assert.That(submitError.Exception, Is.InstanceOf<System.Net.Http.HttpRequestException>());
    }

    [Test]
    public void SingleWorkingTargetLogsNoWarning()
    {
        using var server = new FakeReportServer();

        var warnings = RunBackupReport(null, server.Url("ok"));

        Assert.That(server.RequestPaths.ToArray(), Is.EqualTo(new[] { "/ok" }));
        Assert.That(warnings, Is.Empty);
    }
}
