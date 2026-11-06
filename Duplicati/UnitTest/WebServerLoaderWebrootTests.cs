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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using NUnit.Framework;
using Program = Duplicati.Server.Program;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the <c>--webservice-webroot</c> option decides the folder the web server serves
/// </summary>
[TestFixture]
public class WebServerLoaderWebrootTests
{
    private sealed class TestApplicationSettings(string dataFolder) : IApplicationSettings
    {
        public Action StartOrStopUsageReporter { get; set; }
        public string DataFolder => dataFolder;
        public string Origin { get; set; } = "UnitTest";
        public CancellationToken ApplicationExit => CancellationToken.None;
        public void SignalApplicationExit() { }
        public ISecretProvider SecretProvider { get; set; }
        public ISecretProvider DefaultSecretProvider { get; set; }
        public bool SettingsEncryptionKeyProvidedExternally { get; set; }
    }

    private string dataFolder = string.Empty;

    [SetUp]
    public void Setup()
    {
        this.dataFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this.dataFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(this.dataFolder, true);
        }
        catch
        {
            // If there's an exception, let the OS deal with cleaning up the temp folder.
        }
    }

    [Test]
    [Category("Startup")]
    public async Task WebrootOptionIsUsedInDebugBuilds()
    {
#if !DEBUG
        Assert.Ignore("Release builds only accept a folder inside the installation folder");
#endif
        var webroot = Path.Combine(this.dataFolder, "custom-webroot");
        Directory.CreateDirectory(webroot);

        var options = new Dictionary<string, string>
        {
            // Prevent interference from a default secret provider on the test machine
            { "disable-default-secret-provider", "true" },
            { WebServerLoader.OPTION_WEBROOT, webroot }
        };

        using var connection = Program.GetDatabaseConnection(new TestApplicationSettings(this.dataFolder), options, true, false);

        // The callback stands in for the server, so nothing listens on the port
        WebServerLoader.ParsedWebserverSettings settings = null;
        await WebServerLoader.TryRunServerAsync(options, connection, s =>
        {
            settings = s;
            return Task.FromResult(0);
        });

        Assert.That(settings?.WebRoot, Is.EqualTo(webroot));
    }
}
