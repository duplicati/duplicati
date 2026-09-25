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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.RemoteControl;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class RemoteControlCommandPathTests
    {
        private static readonly Uri BaseAddress = new Uri("http://127.0.0.1:8200");

        [Test]
        [Category("RemoteControl")]
        [TestCase("/", "http://127.0.0.1:8200/")]
        [TestCase("/api/v1/backups", "http://127.0.0.1:8200/api/v1/backups")]
        [TestCase("/api/v1/backups?x=http://example.com/", "http://127.0.0.1:8200/api/v1/backups?x=http://example.com/")]
        [TestCase("/api/v1/../v1/backups", "http://127.0.0.1:8200/api/v1/backups")]
        public void LocalPathsAreAccepted(string path, string expected)
        {
            Assert.That(KeepRemoteConnection.CommandMessage.TryGetLocalTarget(BaseAddress, path, out var target), Is.True);
            Assert.That(target.AbsoluteUri, Is.EqualTo(expected));
        }

        [Test]
        [Category("RemoteControl")]
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("api/v1/backups")]
        [TestCase("http://example.com/api/v1/backups")]
        [TestCase("https://example.com/")]
        [TestCase("http://127.0.0.1:8200@example.com/")]
        [TestCase("http://127.0.0.1:8201/")]
        [TestCase("//example.com/api/v1/backups")]
        [TestCase("/\\example.com/api/v1/backups")]
        [TestCase("\\\\example.com\\share")]
        [TestCase("\\/example.com/")]
        [TestCase(" //example.com/")]
        [TestCase("file:///etc/passwd")]
        [TestCase("@example.com/")]
        [TestCase(":8201/")]
        public void NonLocalPathsAreRejected(string path)
        {
            Assert.That(KeepRemoteConnection.CommandMessage.TryGetLocalTarget(BaseAddress, path, out var target), Is.False);
            Assert.That(target, Is.Null);
        }

        [Test]
        [Category("RemoteControl")]
        [TestCase("Accept")]
        [TestCase("accept-language")]
        [TestCase("Content-Type")]
        [TestCase("content-type")]
        [TestCase("If-None-Match")]
        [TestCase("X-UI-Language")]
        public void SafeHeadersAreForwarded(string name)
        {
            Assert.That(KeepRemoteConnection.CommandMessage.IsAllowedRequestHeader(name), Is.True);
        }

        [Test]
        [Category("RemoteControl")]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("Authorization")]
        [TestCase("authorization")]
        [TestCase("Proxy-Authorization")]
        [TestCase("Cookie")]
        [TestCase("Host")]
        [TestCase("X-Duplicati-PreSharedKey")]
        [TestCase("X-Real-IP")]
        [TestCase("X-Real-Port")]
        [TestCase("X-Forwarded-For")]
        [TestCase("X-Forwarded-Prefix")]
        [TestCase("X-Syno-Token")]
        [TestCase("Transfer-Encoding")]
        [TestCase("Accept-Encoding")]
        [TestCase("Accept ")]
        public void OtherHeadersAreDropped(string name)
        {
            Assert.That(KeepRemoteConnection.CommandMessage.IsAllowedRequestHeader(name), Is.False);
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public HttpRequestMessage Request;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) });
            }
        }

        private static async Task<(CapturingHandler Handler, CommandResponseMessage Response)> ForwardAsync(CommandRequestMessage message)
        {
            var handler = new CapturingHandler();
            using var client = new HttpClient(handler) { BaseAddress = BaseAddress };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-token");

            CommandResponseMessage response = null;
            await new KeepRemoteConnection.CommandMessage(message, r => { response = r; return true; }).HandleAsync(client);
            return (handler, response);
        }

        [Test]
        [Category("RemoteControl")]
        public async Task HandleAsyncDropsDisallowedHeaders()
        {
            var (handler, response) = await ForwardAsync(new CommandRequestMessage("POST", "/api/v1/backups", Convert.ToBase64String(new byte[] { 1 }), new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer remote-token",
                ["Host"] = "example.com",
                ["X-Real-IP"] = "10.0.0.1",
                ["Accept"] = "application/json",
                ["content-type"] = "application/json; charset=utf-8"
            }));

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(handler.Request.RequestUri, Is.EqualTo(new Uri(BaseAddress, "/api/v1/backups")));
            Assert.That(handler.Request.Headers.Authorization.ToString(), Is.EqualTo("Bearer local-token"));
            Assert.That(handler.Request.Headers.Host, Is.Null);
            Assert.That(handler.Request.Headers.Contains("X-Real-IP"), Is.False);
            Assert.That(handler.Request.Headers.Accept.ToString(), Is.EqualTo("application/json"));
            Assert.That(handler.Request.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
        }

        [Test]
        [Category("RemoteControl")]
        public async Task HandleAsyncRejectsNonLocalPathWithoutSending()
        {
            var (handler, response) = await ForwardAsync(new CommandRequestMessage("GET", "http://example.com/", null, null));

            Assert.That(response.StatusCode, Is.EqualTo(400));
            Assert.That(handler.Request, Is.Null);
        }

        [Test]
        [Category("RemoteControl")]
        public void MissingBaseAddressIsRejected()
        {
            Assert.That(KeepRemoteConnection.CommandMessage.TryGetLocalTarget(null, "/api/v1/backups", out _), Is.False);
        }
    }
}
