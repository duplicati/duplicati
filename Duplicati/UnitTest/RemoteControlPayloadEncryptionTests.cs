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
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Duplicati.Library.RemoteControl;
using Jose;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// End-to-end encryption of command payloads (protocol version 2). The portal side is simulated with
    /// the reference implementation in <see cref="CommandPayloadEncryption"/>, so these tests pin the wire
    /// format the frontends must produce.
    /// </summary>
    public class RemoteControlPayloadEncryptionTests
    {
        private static readonly RSA ClientKey = RSA.Create(2048);
        private static readonly RSA PortalKey = RSA.Create(2048);
        private const string MessageId = "428411b1-e8a0-4e5b-91b2-52cdd3b10fc3";

        /// <summary>
        /// The wire uses camelCase, as the frontends produce it
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

        private static Jwk PortalPublicJwk => new Jwk(PortalKey, false);

        private static CommandRequestMessage SampleRequest
            => new CommandRequestMessage("POST", "/api/v1/backups?filter=patient", Convert.ToBase64String(new byte[] { 1, 2, 3 }), new Dictionary<string, string> { { "Content-Type", "application/json" } });

        /// <summary>
        /// Records compare dictionaries by reference, so the fields are compared one by one
        /// </summary>
        private static void AssertSameRequest(CommandRequestMessage actual, CommandRequestMessage expected)
        {
            Assert.That(actual.Method, Is.EqualTo(expected.Method));
            Assert.That(actual.Path, Is.EqualTo(expected.Path));
            Assert.That(actual.Body, Is.EqualTo(expected.Body));
            Assert.That(actual.Headers, Is.EquivalentTo(expected.Headers!));
        }

        private static void AssertSameResponse(CommandResponseMessage actual, CommandResponseMessage expected)
        {
            Assert.That(actual.StatusCode, Is.EqualTo(expected.StatusCode));
            Assert.That(actual.Body, Is.EqualTo(expected.Body));
            if (expected.Headers == null)
                Assert.That(actual.Headers, Is.Null);
            else
                Assert.That(actual.Headers, Is.EquivalentTo(expected.Headers));
        }

        [Test]
        [Category("RemoteControl")]
        public void RequestAndResponseRoundTrip()
        {
            var wrapper = CommandPayloadEncryption.EncryptRequest(MessageId, SampleRequest, PortalPublicJwk, ClientKey);
            var payload = CommandPayloadEncryption.ToPayloadString(wrapper);

            // The wire format is the versioned wrapper with a compact JWE
            using (var json = JsonDocument.Parse(payload))
            {
                Assert.That(json.RootElement.GetProperty("v").GetInt32(), Is.EqualTo(2));
                Assert.That(json.RootElement.GetProperty("jwe").GetString()!.Split('.'), Has.Length.EqualTo(5));
            }
            Assert.That(CommandPayloadEncryption.IsEncrypted(payload), Is.True);

            var request = CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out var replyKey);
            AssertSameRequest(request, SampleRequest);

            var response = new CommandResponseMessage(200, Convert.ToBase64String(new byte[] { 9, 8, 7 }), new Dictionary<string, string> { { "Content-Type", "application/json" } });
            var responsePayload = CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptResponse(MessageId, response, replyKey));

            Assert.That(CommandPayloadEncryption.IsEncrypted(responsePayload), Is.True);
            AssertSameResponse(CommandPayloadEncryption.DecryptResponse(responsePayload, PortalKey, MessageId), response);
        }

        [Test]
        [Category("RemoteControl")]
        public void ResponseIsNotReadableWithTheClientKey()
        {
            CommandPayloadEncryption.DecryptRequest(CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptRequest(MessageId, SampleRequest, PortalPublicJwk, ClientKey)), ClientKey, MessageId, out var replyKey);
            var responsePayload = CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptResponse(MessageId, new CommandResponseMessage(200, null, null), replyKey));

            Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptResponse(responsePayload, ClientKey, MessageId));
        }

        [Test]
        [Category("RemoteControl")]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("null")]
        [TestCase("not json")]
        [TestCase("{\"method\":\"GET\",\"path\":\"/api/v1/backups\",\"body\":null,\"headers\":null}")]
        [TestCase("{\"v\":1,\"jwe\":\"a.b.c.d.e\"}")]
        [TestCase("{\"v\":3,\"jwe\":\"a.b.c.d.e\"}")]
        [TestCase("{\"v\":\"2\",\"jwe\":\"a.b.c.d.e\"}")]
        [TestCase("{\"v\":2}")]
        [TestCase("{\"v\":2,\"jwe\":\"\"}")]
        [TestCase("[]")]
        public void PlainTextAndUnsupportedWrappersAreRefused(string? payload)
        {
            Assert.That(CommandPayloadEncryption.IsEncrypted(payload), Is.False);
            var ex = Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out _));
            Assert.That(ex!.Message, Does.Contain("not end-to-end encrypted"));
        }

        [Test]
        [Category("RemoteControl")]
        public void RequestEncryptedToAnotherKeyIsRefused()
        {
            var payload = CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptRequest(MessageId, SampleRequest, PortalPublicJwk, RSA.Create(2048)));

            Assert.That(CommandPayloadEncryption.IsEncrypted(payload), Is.True);
            Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out _));
        }

        [Test]
        [Category("RemoteControl")]
        public void OtherAlgorithmsAreRefused()
        {
            // A JWE the client key can technically open, but with an algorithm the format does not allow
            var plaintext = JsonSerializer.Serialize(new CommandPayloadEncryption.EncryptedRequest(MessageId,
                JsonSerializer.Deserialize<JsonElement>(PortalPublicJwk.ToJson(JWT.DefaultSettings.JsonMapper)), SampleRequest), JsonOptions);
            var jwe = JWT.Encode(plaintext, new Jwk(ClientKey, false), JweAlgorithm.RSA_OAEP_256, JweEncryption.A256CBC_HS512);
            var payload = CommandPayloadEncryption.ToPayloadString(new CommandPayloadEncryption.EncryptedPayload(2, jwe, null));

            Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out _));
        }

        private static string EncryptWithReplyKeyJson(string replyKeyJson)
        {
            var plaintext = "{\"messageId\":\"" + MessageId + "\",\"replyKey\":" + replyKeyJson + ",\"request\":" + JsonSerializer.Serialize(SampleRequest, JsonOptions) + "}";
            var jwe = JWT.Encode(plaintext, new Jwk(ClientKey, false), CommandPayloadEncryption.KeyAlgorithm, CommandPayloadEncryption.ContentEncryption);
            return CommandPayloadEncryption.ToPayloadString(new CommandPayloadEncryption.EncryptedPayload(2, jwe, null));
        }

        [Test]
        [Category("RemoteControl")]
        public void ReplyKeyMustBeAnRsaPublicKey()
        {
            var ecKey = new Jwk(ECDsa.Create(ECCurve.NamedCurves.nistP256), false).ToJson(JWT.DefaultSettings.JsonMapper);
            var privateKey = new Jwk(PortalKey, true).ToJson(JWT.DefaultSettings.JsonMapper);

            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(EncryptWithReplyKeyJson("null"), ClientKey, MessageId, out _))!.Message, Does.Contain("missing"));
            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(EncryptWithReplyKeyJson(ecKey), ClientKey, MessageId, out _))!.Message, Does.Contain("not an RSA key"));
            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(EncryptWithReplyKeyJson(privateKey), ClientKey, MessageId, out _))!.Message, Does.Contain("public key"));
            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(EncryptWithReplyKeyJson("{\"kty\":\"RSA\",\"n\":\"AQAB\"}"), ClientKey, MessageId, out _))!.Message, Does.Contain("incomplete"));
            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(EncryptWithReplyKeyJson("{\"kty\":\"RSA\",\"n\":\"!!\",\"e\":\"AQAB\"}"), ClientKey, MessageId, out _))!.Message, Does.Contain("invalid"));
        }

        /// <summary>
        /// WebCrypto's exportKey("jwk") adds alg, ext and key_ops next to kty, n and e; the client must accept that shape
        /// </summary>
        [Test]
        [Category("RemoteControl")]
        public void WebCryptoShapedReplyKeyIsAccepted()
        {
            var parameters = PortalKey.ExportParameters(false);
            var n = Base64Url.Encode(parameters.Modulus!);
            var e = Base64Url.Encode(parameters.Exponent!);
            var webCryptoJwk = "{\"alg\":\"RSA-OAEP-256\",\"e\":\"" + e + "\",\"ext\":true,\"key_ops\":[\"encrypt\"],\"kty\":\"RSA\",\"n\":\"" + n + "\"}";

            var request = CommandPayloadEncryption.DecryptRequest(EncryptWithReplyKeyJson(webCryptoJwk), ClientKey, MessageId, out var replyKey);
            AssertSameRequest(request, SampleRequest);

            var response = new CommandResponseMessage(204, null, null);
            var responsePayload = CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptResponse(MessageId, response, replyKey));
            AssertSameResponse(CommandPayloadEncryption.DecryptResponse(responsePayload, PortalKey, MessageId), response);
        }

        /// <summary>
        /// The message id inside the ciphertext must match the envelope, so a relay cannot move a request to
        /// another message or answer a request with the response to another
        /// </summary>
        [Test]
        [Category("RemoteControl")]
        public void PayloadBoundToAnotherMessageIsRefusedInBothDirections()
        {
            var payload = CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptRequest(MessageId, SampleRequest, PortalPublicJwk, ClientKey));
            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(payload, ClientKey, "another-message", out _))!.Message, Does.Contain("another message"));

            CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out var replyKey);
            var responsePayload = CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptResponse("another-message", new CommandResponseMessage(200, null, null), replyKey));
            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptResponse(responsePayload, PortalKey, MessageId))!.Message, Does.Contain("another message"));

            // A response without any binding is not accepted either
            var unbound = JWT.Encode("{\"statusCode\":200}", new Jwk(PortalKey, false), CommandPayloadEncryption.KeyAlgorithm, CommandPayloadEncryption.ContentEncryption);
            Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptResponse(CommandPayloadEncryption.ToPayloadString(new CommandPayloadEncryption.EncryptedPayload(2, unbound, null)), PortalKey, MessageId));
        }

        private static string GzipRequestPayload(string plaintext)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write(Encoding.UTF8.GetBytes(plaintext));
            var jwe = JWT.EncodeBytes(output.ToArray(), new Jwk(ClientKey, false), CommandPayloadEncryption.KeyAlgorithm, CommandPayloadEncryption.ContentEncryption);
            return CommandPayloadEncryption.ToPayloadString(new CommandPayloadEncryption.EncryptedPayload(2, jwe, CommandPayloadEncryption.GzipCompression));
        }

        private static string RequestPlaintext()
            => "{\"messageId\":\"" + MessageId + "\",\"replyKey\":" + PortalPublicJwk.ToJson(JWT.DefaultSettings.JsonMapper) + ",\"request\":" + JsonSerializer.Serialize(SampleRequest, JsonOptions) + "}";

        /// <summary>
        /// The sender decides on compression by size, and says so in the wrapper; small plaintexts stay as they are
        /// </summary>
        [Test]
        [Category("RemoteControl")]
        public void SmallResponsesAreNotCompressed()
        {
            CommandPayloadEncryption.DecryptRequest(CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptRequest(MessageId, SampleRequest, PortalPublicJwk, ClientKey)), ClientKey, MessageId, out var replyKey);
            var response = new CommandResponseMessage(200, Convert.ToBase64String(new byte[] { 1, 2, 3 }), null);

            var wrapper = CommandPayloadEncryption.EncryptResponse(MessageId, response, replyKey);
            var payload = CommandPayloadEncryption.ToPayloadString(wrapper);

            Assert.That(wrapper.Zip, Is.Null);
            Assert.That(payload, Does.Not.Contain("\"zip\""));
            AssertSameResponse(CommandPayloadEncryption.DecryptResponse(payload, PortalKey, MessageId), response);
        }

        [Test]
        [Category("RemoteControl")]
        public void LargeResponsesAreGzipCompressedAndSmallerOnTheWire()
        {
            CommandPayloadEncryption.DecryptRequest(CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptRequest(MessageId, SampleRequest, PortalPublicJwk, ClientKey)), ClientKey, MessageId, out var replyKey);
            // JSON-like, compressible content of ~300 KB, as systeminfo or a file listing would be
            var body = new StringBuilder();
            for (var i = 0; i < 3000; i++)
                body.Append("{\"name\":\"option-").Append(i).Append("\",\"description\":\"A longer description of the option that repeats a lot\"},");
            var response = new CommandResponseMessage(200, Convert.ToBase64String(Encoding.UTF8.GetBytes(body.ToString())), new Dictionary<string, string> { { "Content-Type", "application/json" } });

            var wrapper = CommandPayloadEncryption.EncryptResponse(MessageId, response, replyKey);
            var payload = CommandPayloadEncryption.ToPayloadString(wrapper);

            Assert.That(wrapper.Zip, Is.EqualTo("gzip"));
            using (var json = JsonDocument.Parse(payload))
                Assert.That(json.RootElement.GetProperty("zip").GetString(), Is.EqualTo("gzip"));
            Assert.That(payload.Length, Is.LessThan(response.Body!.Length / 4), "the compressed payload should be far smaller than the body it carries");
            AssertSameResponse(CommandPayloadEncryption.DecryptResponse(payload, PortalKey, MessageId), response);
        }

        [Test]
        [Category("RemoteControl")]
        public void CompressionStartsJustAboveTheThreshold()
        {
            CommandPayloadEncryption.DecryptRequest(CommandPayloadEncryption.ToPayloadString(CommandPayloadEncryption.EncryptRequest(MessageId, SampleRequest, PortalPublicJwk, ClientKey)), ClientKey, MessageId, out var replyKey);

            // The plaintext is the serialized {messageId, response}; size the body so the plaintext lands on either side of the threshold
            string PlaintextFor(CommandResponseMessage r) => JsonSerializer.Serialize(new CommandPayloadEncryption.EncryptedResponse(MessageId, r), JsonOptions);
            var below = new CommandResponseMessage(200, new string('a', 100), null);
            below = below with { Body = new string('a', 100 + CommandPayloadEncryption.CompressionThresholdBytes - Encoding.UTF8.GetByteCount(PlaintextFor(below))) };
            var above = below with { Body = below.Body + "a" };

            Assert.That(Encoding.UTF8.GetByteCount(PlaintextFor(below)), Is.EqualTo(CommandPayloadEncryption.CompressionThresholdBytes));
            Assert.That(CommandPayloadEncryption.EncryptResponse(MessageId, below, replyKey).Zip, Is.Null);
            Assert.That(CommandPayloadEncryption.EncryptResponse(MessageId, above, replyKey).Zip, Is.EqualTo("gzip"));
        }

        /// <summary>
        /// The portal compresses large requests the same way, for example a restore that names many paths
        /// </summary>
        [Test]
        [Category("RemoteControl")]
        public void LargeRequestsAreGzipCompressedAndSmallerOnTheWire()
        {
            var paths = new List<string>();
            for (var i = 0; i < 500; i++)
                paths.Add($"/home/user/documents/project-{i}/report-final-v{i % 7}.docx");
            var restore = new CommandRequestMessage("POST", "/api/v1/backup/1/restore", Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { paths }))), null);

            var wrapper = CommandPayloadEncryption.EncryptRequest(MessageId, restore, PortalPublicJwk, ClientKey);
            var payload = CommandPayloadEncryption.ToPayloadString(wrapper);

            Assert.That(wrapper.Zip, Is.EqualTo("gzip"));
            Assert.That(payload.Length, Is.LessThan(restore.Body!.Length / 2));
            var request = CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out _);
            Assert.That(request.Method, Is.EqualTo(restore.Method));
            Assert.That(request.Path, Is.EqualTo(restore.Path));
            Assert.That(request.Body, Is.EqualTo(restore.Body));
            Assert.That(request.Headers, Is.Null);
        }

        [Test]
        [Category("RemoteControl")]
        public void CompressedRequestsAreAccepted()
        {
            var request = CommandPayloadEncryption.DecryptRequest(GzipRequestPayload(RequestPlaintext()), ClientKey, MessageId, out _);
            AssertSameRequest(request, SampleRequest);
        }

        [Test]
        [Category("RemoteControl")]
        public void UnsupportedCompressionIsRefused()
        {
            var jwe = JWT.Encode(RequestPlaintext(), new Jwk(ClientKey, false), CommandPayloadEncryption.KeyAlgorithm, CommandPayloadEncryption.ContentEncryption);
            var payload = CommandPayloadEncryption.ToPayloadString(new CommandPayloadEncryption.EncryptedPayload(2, jwe, "br"));

            Assert.That(CommandPayloadEncryption.IsEncrypted(payload), Is.True);
            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out _))!.Message, Does.Contain("unsupported compression"));
        }

        [Test]
        [Category("RemoteControl")]
        public void CorruptCompressedDataIsRefused()
        {
            var jwe = JWT.EncodeBytes(Encoding.UTF8.GetBytes("this is not gzip"), new Jwk(ClientKey, false), CommandPayloadEncryption.KeyAlgorithm, CommandPayloadEncryption.ContentEncryption);
            var payload = CommandPayloadEncryption.ToPayloadString(new CommandPayloadEncryption.EncryptedPayload(2, jwe, "gzip"));

            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out _))!.Message, Does.Contain("decompressed"));
        }

        [Test]
        [Category("RemoteControl")]
        public void DecompressionIsCapped()
        {
            // A few tens of KB of gzip that inflate to just over the limit
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                var zeros = new byte[1024 * 1024];
                for (var i = 0; i <= CommandPayloadEncryption.MaxDecompressedBytes / zeros.Length; i++)
                    gzip.Write(zeros);
            }
            var jwe = JWT.EncodeBytes(output.ToArray(), new Jwk(ClientKey, false), CommandPayloadEncryption.KeyAlgorithm, CommandPayloadEncryption.ContentEncryption);
            var payload = CommandPayloadEncryption.ToPayloadString(new CommandPayloadEncryption.EncryptedPayload(2, jwe, "gzip"));

            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out _))!.Message, Does.Contain("exceeds"));
        }

        [Test]
        [Category("RemoteControl")]
        public void MissingRequestIsRefused()
        {
            var plaintext = "{\"messageId\":\"" + MessageId + "\",\"replyKey\":" + PortalPublicJwk.ToJson(JWT.DefaultSettings.JsonMapper) + "}";
            var jwe = JWT.Encode(plaintext, new Jwk(ClientKey, false), CommandPayloadEncryption.KeyAlgorithm, CommandPayloadEncryption.ContentEncryption);
            var payload = CommandPayloadEncryption.ToPayloadString(new CommandPayloadEncryption.EncryptedPayload(2, jwe, null));

            Assert.That(Assert.Throws<CommandPayloadException>(() => CommandPayloadEncryption.DecryptRequest(payload, ClientKey, MessageId, out _))!.Message, Does.Contain("no request"));
        }
    }
}
