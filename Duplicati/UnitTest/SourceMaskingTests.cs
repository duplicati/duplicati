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
using Duplicati.Server;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class SourceMaskingTests
    {
        private static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password",
            "auth-password",
            "auth_password",
            "secret"
        };

        [Test]
        public void IsSpecialSource_ValidFormat_ReturnsTrue()
        {
            Assert.That(SourceMasking.IsSpecialSource("@/path|url://test"), Is.True);
            Assert.That(SourceMasking.IsSpecialSource("@/path/sub|url://test"), Is.True);
            Assert.That(SourceMasking.IsSpecialSource("@/|url://test"), Is.True);
        }

        [Test]
        public void IsSpecialSource_InvalidFormat_ReturnsFalse()
        {
            Assert.That(SourceMasking.IsSpecialSource("/path/only"), Is.False);
            Assert.That(SourceMasking.IsSpecialSource("url://test"), Is.False);
            Assert.That(SourceMasking.IsSpecialSource("@/path/no/separator"), Is.False);
            Assert.That(SourceMasking.IsSpecialSource("no/at/prefix|url://test"), Is.False);
            Assert.That(SourceMasking.IsSpecialSource(null), Is.False);
            Assert.That(SourceMasking.IsSpecialSource(""), Is.False);
        }

        [Test]
        public void ExtractPathPrefix_ValidFormat_ReturnsPrefix()
        {
            Assert.That(SourceMasking.ExtractPathPrefix("@/path|url://test"), Is.EqualTo("@/path"));
            Assert.That(SourceMasking.ExtractPathPrefix("@/a/b/c|url://test"), Is.EqualTo("@/a/b/c"));
        }

        [Test]
        public void ExtractUrl_ValidFormat_ReturnsUrl()
        {
            Assert.That(SourceMasking.ExtractUrl("@/path|url://test"), Is.EqualTo("url://test"));
            Assert.That(SourceMasking.ExtractUrl("@/path|url://user:pass@host/path?q=1"), Is.EqualTo("url://user:pass@host/path?q=1"));
        }

        [Test]
        public void ReplaceUrl_ValidFormat_ReturnsNewSource()
        {
            var source = "@/path|url://old";
            var newUrl = "url://new";
            Assert.That(SourceMasking.ReplaceUrl(source, newUrl), Is.EqualTo("@/path|url://new"));
        }

        [Test]
        public void IsMaskedUrl_ContainsPlaceholder_ReturnsTrue()
        {
            Assert.That(SourceMasking.IsMaskedUrl("url://user:" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER + "@host"), Is.True);
            Assert.That(SourceMasking.IsMaskedUrl("url://host?password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER), Is.True);
        }

        [Test]
        public void IsMaskedUrl_NoPlaceholder_ReturnsFalse()
        {
            Assert.That(SourceMasking.IsMaskedUrl("url://user:pass@host"), Is.False);
            Assert.That(SourceMasking.IsMaskedUrl("url://host?password=pass"), Is.False);
        }

        [Test]
        public void MaskSource_SpecialSource_MasksUrl()
        {
            var source = "@/path|url://host?password=secret123&other=value";
            var masked = SourceMasking.MaskSource(source, ProtectedNames);

            Assert.That(masked, Does.Contain("password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER));
            Assert.That(masked, Does.Contain("other=value"));
            Assert.That(masked, Does.StartWith("@/path|"));
        }
        [Test]
        public void MaskSource_RegularSource_ReturnsUnchanged()
        {
            var source = "/regular/path";
            var masked = SourceMasking.MaskSource(source, ProtectedNames);
            Assert.That(masked, Is.EqualTo(source));
        }

        [Test]
        public void MaskSources_MixedArray_MasksOnlySpecial()
        {
            var sources = new[]
            {
                "/regular/path",
                "@/special|url://host?password=secret"
            };

            var masked = SourceMasking.MaskSources(sources, ProtectedNames);

            Assert.That(masked[0], Is.EqualTo("/regular/path"));
            Assert.That(masked[1], Does.Contain("password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER));
        }

        [Test]
        public void UnmaskSources_SameOrder_RestoresValues()
        {
            var previous = new[] { "@/path|url://host?password=secret" };
            var current = new[] { "@/path|url://host?password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER };

            var unmasked = SourceMasking.UnmaskSources(current, previous);

            Assert.That(unmasked[0], Is.EqualTo(previous[0]));
        }

        [Test]
        public void UnmaskSources_Reordered_RestoresCorrectly()
        {
            var previous = new[]
            {
                "@/path1|url://host?password=secret1",
                "@/path2|url://host?password=secret2"
            };

            // Swapped order and masked
            var current = new[]
            {
                "@/path2|url://host?password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER,
                "@/path1|url://host?password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER
            };

            var unmasked = SourceMasking.UnmaskSources(current, previous);

            Assert.That(unmasked[0], Is.EqualTo(previous[1]));
            Assert.That(unmasked[1], Is.EqualTo(previous[0]));
        }

        [Test]
        public void UnmaskSources_NewSource_KeepsAsIs()
        {
            var previous = new[] { "@/path1|url://host?password=secret1" };
            var current = new[]
            {
                "@/path1|url://host?password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER,
                "@/path2|url://host?password=newsecret" // Not masked
            };

            var unmasked = SourceMasking.UnmaskSources(current, previous);

            Assert.That(unmasked[0], Is.EqualTo(previous[0]));
            Assert.That(unmasked[1], Is.EqualTo(current[1]));
        }

        [Test]
        public void UnmaskSources_RemovedSource_Ignores()
        {
            var previous = new[]
            {
                "@/path1|url://host?password=secret1",
                "@/path2|url://host?password=secret2"
            };

            var current = new[] { "@/path1|url://host?password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER };

            var unmasked = SourceMasking.UnmaskSources(current, previous);

            Assert.That(unmasked.Length, Is.EqualTo(1));
            Assert.That(unmasked[0], Is.EqualTo(previous[0]));
        }

        [Test]
        public void UnmaskSources_MaskedButMissingPrevious_ThrowsException()
        {
            var previous = new string[0];
            var current = new[] { "@/path1|url://host?password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER };

            Assert.Throws<InvalidOperationException>(() =>
                SourceMasking.UnmaskSources(current, previous));
        }

        [Test]
        public void UnmaskSources_PartialUnmasking_RestoresOnlyMaskedParts()
        {
            // Previous had secret1
            var previous = new[] { "@/path|url://host?password=secret1&other=old" };

            // Current has masked p, but changed other param
            var current = new[] { "@/path|url://host?password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER + "&other=new" };

            var unmasked = SourceMasking.UnmaskSources(current, previous);

            // Should have restored secret1 but kept other=new
            Assert.That(unmasked[0], Does.Contain("password=secret1"));
            Assert.That(unmasked[0], Does.Contain("other=new"));
        }

        [Test]
        public void UnmaskSources_SingleSourceFallback_RestoresEvenIfPathChanged()
        {
            // Previous had path1
            var previous = new[] { "@/path1|url://host?password=secret1" };

            // Current has path2 (renamed) but masked URL
            var current = new[] { "@/path2|url://host?password=" + Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER };

            var unmasked = SourceMasking.UnmaskSources(current, previous);

            // Should have restored secret1 from the single previous source
            Assert.That(unmasked[0], Does.Contain("password=secret1"));
            Assert.That(unmasked[0], Does.StartWith("@/path2|"));
        }
    }
}
