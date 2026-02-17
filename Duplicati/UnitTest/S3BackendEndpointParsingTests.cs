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
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

[TestFixture]
public class S3BackendEndpointParsingTests
{
    private static readonly string[] SupportedClients = ["aws", "minio"];

    private static Dictionary<string, string?> CreateOptions(string serverName, string client)
        => new()
        {
            ["aws-access-key-id"] = "test-access-key",
            ["aws-secret-access-key"] = "test-secret-key",
            ["s3-server-name"] = serverName,
            ["s3-client"] = client
        };

    [TestCase("server.host")]
    [TestCase("server.host:9000")]
    [TestCase("http://server.host")]
    [TestCase("http://server.host:9000")]
    [TestCase("https://server.host")]
    [TestCase("https://server.host:9000")]
    [Category("Backend")]
    public void AcceptsExpectedEndpointFormats(string endpoint)
    {
        foreach (var client in SupportedClients)
        {
            Assert.DoesNotThrow(() =>
            {
                using var backend = new S3("s3://test-bucket/", CreateOptions(endpoint, client));
            }, $"Expected endpoint '{endpoint}' to be accepted for s3-client '{client}'");
        }
    }

    [TestCase("http://server.host/path")]
    [TestCase("https://server.host/path")]
    [TestCase("server.host/path")]
    [TestCase("server.host\\path")]
    [Category("Backend")]
    public void RejectsEndpointsContainingPaths(string endpoint)
    {
        foreach (var client in SupportedClients)
        {
            var ex = Assert.Throws<UserInformationException>(() =>
            {
                using var backend = new S3("s3://test-bucket/", CreateOptions(endpoint, client));
            }, $"Expected endpoint '{endpoint}' to be rejected for s3-client '{client}'");

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.HelpID, Is.EqualTo("S3NoPathInEndpoint"));
        }
    }
}
