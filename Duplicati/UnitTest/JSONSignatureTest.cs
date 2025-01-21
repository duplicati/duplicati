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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

[Category("SignatureTest")]
public class JSONSignatureTest
{
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void SignWithASingleKeyShouldWork(bool withHeaders)
    {
        var content = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { test = "test", extra = 234 }));
        var headers = withHeaders ? new System.Collections.Generic.Dictionary<string, string> { { "test", "1234" } } : null;
        var key = new RSACryptoServiceProvider(2048);

        using var source = new MemoryStream(content);
        using var target = new MemoryStream();

        JSONSignature.SignAsync(source, target, [new JSONSignature.SignOperation(JSONSignature.RSA_SHA256, key.ToXmlString(false), key.ToXmlString(true), headers)]).Await();

        target.Position = 0;

        var valids = JSONSignature.Verify(target, [new JSONSignature.VerifyOperation(JSONSignature.RSA_SHA256, key.ToXmlString(false))]);
        Assert.That(valids, Has.Count.EqualTo(1));
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void SignWithMultipleAlgsShouldWork(bool withHeaders)
    {
        var content = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { test = "test", extra = 234 }));
        var headers = withHeaders ? new System.Collections.Generic.Dictionary<string, string> { { "test", "1234" } } : null;
        var key = new RSACryptoServiceProvider(2048);

        using var source = new MemoryStream(content);
        using var target = new MemoryStream();

        var algs = new[] { JSONSignature.RSA_SHA256, JSONSignature.RSA_SHA384, JSONSignature.RSA_SHA512 };

        JSONSignature.SignAsync(source, target, algs.Select(x => new JSONSignature.SignOperation(x, key.ToXmlString(false), key.ToXmlString(true), headers))).Await();

        target.Position = 0;
        var valids = JSONSignature.Verify(target, algs.Select(x => new JSONSignature.VerifyOperation(x, key.ToXmlString(false))));
        Assert.That(valids, Has.Count.EqualTo(algs.Length));
        foreach (var alg in algs)
        {
            target.Position = 0;
            var valid = JSONSignature.Verify(target, [new JSONSignature.VerifyOperation(alg, key.ToXmlString(false))]);
            Assert.That(valid, Has.Count.EqualTo(1));
            Assert.That(valid.First().Algorithm, Is.EqualTo(alg));
            Assert.That(valid.First().PublicKey, Is.EqualTo(key.ToXmlString(false)));

            target.Position = 0;
            var hasOne = JSONSignature.VerifyAtLeastOne(target, [new JSONSignature.VerifyOperation(alg, key.ToXmlString(false))]);
            Assert.That(hasOne, Is.True);
        }
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void SignWithMultipleKeysShouldWork(bool withHeaders)
    {
        var content = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { test = "test", extra = 234 }));
        var headers = withHeaders ? new System.Collections.Generic.Dictionary<string, string> { { "test", "1234" } } : null;
        var keys = new[] { new RSACryptoServiceProvider(2048), new RSACryptoServiceProvider(2048), new RSACryptoServiceProvider(1024) };

        using var source = new MemoryStream(content);
        using var target = new MemoryStream();

        var algs = new[] { JSONSignature.RSA_SHA256, JSONSignature.RSA_SHA384, JSONSignature.RSA_SHA512 };
        var algkeycombos = algs.SelectMany(alg => keys.Select(key => (Alg: alg, Key: key))).ToArray();

        JSONSignature.SignAsync(source, target, algkeycombos.Select(x => new JSONSignature.SignOperation(x.Alg, x.Key.ToXmlString(false), x.Key.ToXmlString(true), headers))).Await();

        target.Position = 0;
        var valids = JSONSignature.Verify(target, algkeycombos.Select(x => new JSONSignature.VerifyOperation(x.Alg, x.Key.ToXmlString(false))));
        Assert.That(valids, Has.Count.EqualTo(algkeycombos.Length));
        foreach (var alg in algkeycombos)
        {
            target.Position = 0;
            var valid = JSONSignature.Verify(target, [new JSONSignature.VerifyOperation(alg.Alg, alg.Key.ToXmlString(false))]);
            Assert.That(valid, Has.Count.EqualTo(1));
            Assert.That(valid.First().Algorithm, Is.EqualTo(alg.Alg));
            Assert.That(valid.First().PublicKey, Is.EqualTo(alg.Key.ToXmlString(false)));

            target.Position = 0;
            var hasOne = JSONSignature.VerifyAtLeastOne(target, [new JSONSignature.VerifyOperation(alg.Alg, alg.Key.ToXmlString(false))]);
            Assert.That(hasOne, Is.True);
        }
    }

    [Test]
    public void InvalidSignaturesShouldNotThrow()
    {
        var broken1 = new[] {
            // Invalid Base64 data
            "//SIGJSONv1: ####.abc=\n{\"x\": 1}"u8.ToArray(),

            // Invalid Base64 data
            "//SIGJSONv1: abc.abc\n{\"x\": 1}"u8.ToArray(),

            // Invalid header
            "//SIGJSONv1:abc=.abc=\n{\"x\": 1}"u8.ToArray(),
            
            // No newline
            "//SIGJSONv1: abc=.abc={\"x\": 1}"u8.ToArray(),

            // No newline
            "//SIGJSONv1: abc=.abc={\"x\": 1}\n"u8.ToArray(),

            // Extra newline
            "//SIGJSONv1: abc=.abc=\n{\"x\": 1}\n"u8.ToArray(),
        };

        var key = new RSACryptoServiceProvider(2048);
        foreach (var c in broken1)
        {
            using var source = new MemoryStream(c);
            using var target = new MemoryStream();

            var valids = JSONSignature.Verify(target, [new JSONSignature.VerifyOperation(JSONSignature.RSA_SHA256, key.ToXmlString(false))]);
            Assert.That(valids, Has.Count.EqualTo(0));
        }
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void TaintedDataShouldNotValidate(bool withHeaders)
    {
        var content = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { test = "test", extra = 234 }));
        var headers = withHeaders ? new System.Collections.Generic.Dictionary<string, string> { { "test", "1234" } } : null;
        var keys = new[] { new RSACryptoServiceProvider(2048), new RSACryptoServiceProvider(2048), new RSACryptoServiceProvider(1024) };

        using var source = new MemoryStream(content);
        using var target = new MemoryStream();

        var algs = new[] { JSONSignature.RSA_SHA256, JSONSignature.RSA_SHA384, JSONSignature.RSA_SHA512 };
        var algkeycombos = algs.SelectMany(alg => keys.Select(key => (Alg: alg, Key: key))).ToArray();

        JSONSignature.SignAsync(source, target, algkeycombos.Select(x => new JSONSignature.SignOperation(x.Alg, x.Key.ToXmlString(false), x.Key.ToXmlString(true), headers))).Await();

        // Taint the data
        target.Position = target.Length - 1;
        target.WriteByte((byte)'\n');

        target.Position = 0;
        var valids = JSONSignature.Verify(target, algkeycombos.Select(x => new JSONSignature.VerifyOperation(x.Alg, x.Key.ToXmlString(false))));
        Assert.That(valids, Has.Count.EqualTo(0));
    }

    [Test]
    [TestCase(true, 1)] // Offset=1 breaks the JSON
    [TestCase(false, 1)]
    [TestCase(true, 4)] // Offset=4 changes the header key name
    [TestCase(false, 4)]
    public void TaintedHeadersShouldNotValidate(bool withHeaders, int offset)
    {
        var content = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { test = "test", extra = 234 }));
        var headers = withHeaders ? new System.Collections.Generic.Dictionary<string, string> { { "test", "1234" } } : null;
        var keys = new[] { new RSACryptoServiceProvider(2048), new RSACryptoServiceProvider(2048), new RSACryptoServiceProvider(1024) };

        using var source = new MemoryStream(content);
        using var target = new MemoryStream();

        var algs = new[] { JSONSignature.RSA_SHA256, JSONSignature.RSA_SHA384, JSONSignature.RSA_SHA512 };
        var algkeycombos = algs.SelectMany(alg => keys.Select(key => (Alg: alg, Key: key))).ToArray();

        JSONSignature.SignAsync(source, target, algkeycombos.Select(x => new JSONSignature.SignOperation(x.Alg, x.Key.ToXmlString(false), x.Key.ToXmlString(true), headers))).Await();

        // Taint the header data for the first signature
        target.Position = "//SIGJSONv1: ".Length + offset;
        var cur = target.ReadByte();
        target.Position -= 1;
        target.WriteByte((byte)(cur - 1));

        target.Position = 0;
        var valids = JSONSignature.Verify(target, algkeycombos.Select(x => new JSONSignature.VerifyOperation(x.Alg, x.Key.ToXmlString(false))));
        Assert.That(valids, Has.Count.EqualTo(algkeycombos.Length - 1));
    }
}
