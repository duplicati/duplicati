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

#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Duplicati.Library.Backend.Backblaze;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class B2DownloadUrlTests
    {
        private static Dictionary<string, string?> CreateOptions(string? customDownloadUrl = null)
        {
            var options = new Dictionary<string, string?>
            {
                ["b2-accountid"] = "account-id",
                ["b2-applicationkey"] = "application-key"
            };

            if (customDownloadUrl != null)
                options["b2-download-url"] = customDownloadUrl;

            return options;
        }

        private static string ResolveDownloadUrl(B2 backend, string? defaultDownloadUrl)
        {
            var method = typeof(B2).GetMethod("ResolveDownloadUrl", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Expected helper method ResolveDownloadUrl to exist");

            var value = method!.Invoke(backend, [defaultDownloadUrl]);
            Assert.IsInstanceOf<string>(value);
            return (string)value!;
        }

        [Test]
        [Category("Backblaze")]
        public void UsesCustomB2DownloadUrlWhenProvided()
        {
            var backend = new B2("b2://bucket/prefix", CreateOptions("https://cdn.example.com"));
            var result = ResolveDownloadUrl(backend, "https://f001.backblazeb2.com");

            Assert.AreEqual("https://cdn.example.com", result);
        }

        [Test]
        [Category("Backblaze")]
        public void NormalizesCustomB2DownloadUrl()
        {
            var backend = new B2("b2://bucket/prefix", CreateOptions("  https://cdn.example.com/  "));
            var result = ResolveDownloadUrl(backend, "https://f001.backblazeb2.com");

            Assert.AreEqual("https://cdn.example.com", result);
        }

        [Test]
        [Category("Backblaze")]
        public void FallsBackToAuthDownloadUrlWhenCustomNotProvided()
        {
            var backend = new B2("b2://bucket/prefix", CreateOptions());
            var result = ResolveDownloadUrl(backend, "https://f001.backblazeb2.com");

            Assert.AreEqual("https://f001.backblazeb2.com", result);
        }

        [Test]
        [Category("Backblaze")]
        public void ThrowsForInvalidCustomB2DownloadUrl()
        {
            Assert.Throws<UserInformationException>(() =>
                new B2("b2://bucket/prefix", CreateOptions("not-a-valid-url")));
        }

        [Test]
        [Category("Backblaze")]
        public void ThrowsWhenNoDownloadUrlCanBeResolved()
        {
            var backend = new B2("b2://bucket/prefix", CreateOptions());

            var ex = Assert.Throws<TargetInvocationException>(() => ResolveDownloadUrl(backend, null));
            Assert.IsNotNull(ex?.InnerException);
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException);
        }
    }
}
