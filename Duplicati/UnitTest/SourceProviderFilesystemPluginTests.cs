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
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.SourceProvider.Builtin.HyperV;
using Duplicati.Library.SourceProvider.Builtin.MSSQL;
using Duplicati.WebserverCore.Endpoints.V1.FilesystemPlugins;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The source picker asks every filesystem plugin for its root entries each time
/// the tree root is opened, so a plugin whose application is not installed, or
/// that cannot be used from the current process, must answer without logging.
/// </summary>
[TestFixture]
public class SourceProviderFilesystemPluginTests
{
    private sealed class LogSink : ILogDestination
    {
        public List<LogEntry> Entries { get; } = [];
        public void WriteMessage(LogEntry entry) => Entries.Add(entry);
    }

    private static IEnumerable<IPrefixedSourceProviderModule> Modules()
        => [new HyperVSourceProvider(), new MSSQLSourceProvider()];

    [TestCaseSource(nameof(Modules))]
    public void Root_listing_never_warns(IPrefixedSourceProviderModule module)
    {
        // Whatever this machine has - no Hyper-V or MSSQL, an unelevated process,
        // or the real thing - opening the tree root is not a reason to warn
        var sink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var scope = Log.StartScope(sink, LogMessageType.Warning);

        var plugin = new SourceProviderFilesystemPlugin(module, new Dictionary<string, string?>());
        var entries = plugin.GetEntries([]).ToList();

        Assert.That(sink.Entries, Is.Empty, string.Join("; ", sink.Entries.Select(x => $"{x.Id}: {x.Message}")));
        if (!System.OperatingSystem.IsWindows())
            Assert.That(entries, Is.Empty);
    }
}
