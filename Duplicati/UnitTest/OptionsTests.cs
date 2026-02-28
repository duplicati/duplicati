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
using Duplicati.Library.Main;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class OptionsTests
    {
        private static Options CreateOptions(IDictionary<string, string?>? values = null)
        {
            if (values is null)
                return new Options(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

            return new Options(new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase));
        }

        [Test]
        [Category("Options")]
        public void BlocksizeDefaultsToOneMegabyte()
        {
            var options = CreateOptions();

            Assert.That(options.Blocksize, Is.EqualTo(1024 * 1024));
        }

        [Test]
        [Category("Options")]
        public void BlocksizeThrowsWhenBelowMinimum()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "blocksize", "512b" }
            });

            Assert.That(() => _ = options.Blocksize, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        [Category("Options")]
        public void BlocksizeThrowsWhenAboveMaximum()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "blocksize", "5gb" }
            });

            Assert.That(() => _ = options.Blocksize, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        [Category("Options")]
        public void HasBlocksizeReflectsPresenceOfValue()
        {
            var withoutValue = CreateOptions();
            var withValue = CreateOptions(new Dictionary<string, string?>
            {
                { "blocksize", "8mb" }
            });

            Assert.That(withoutValue.HasBlocksize, Is.False);
            Assert.That(withValue.HasBlocksize, Is.True);
        }

        [Test]
        [Category("Options")]
        public void RestoreCacheMaxDefaultsToRatioOfBlocksize()
        {
            var options = CreateOptions();

            Assert.That(options.RestoreCacheMax, Is.EqualTo(4096));
        }

        [Test]
        [Category("Options")]
        public void RestoreCacheMaxThrowsWhenSmallerThanBlocksize()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "restore-cache-max", "512kb" }
            });

            Assert.That(() => _ = options.RestoreCacheMax, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        [Category("Options")]
        public void RestoreCacheMaxAllowsExplicitZero()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "restore-cache-max", "0" }
            });

            Assert.That(options.RestoreCacheMax, Is.EqualTo(0));
        }

        [Test]
        [Category("Options")]
        public void RestoreCacheEvictDefaultsToFiftyPercent()
        {
            var options = CreateOptions();

            Assert.That(options.RestoreCacheEvict, Is.EqualTo(0.5f));
        }

        [Test]
        [Category("Options")]
        public void RestoreCacheEvictParsesValidPercentage()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "restore-cache-evict", "25" }
            });

            Assert.That(options.RestoreCacheEvict, Is.EqualTo(0.25f));
        }

        [Test]
        [Category("Options")]
        public void RestoreCacheEvictThrowsOnInvalidNumber()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "restore-cache-evict", "abc" }
            });

            Assert.That(() => _ = options.RestoreCacheEvict, Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [Category("Options")]
        public void RestoreCacheEvictThrowsOnPercentageOutOfRange()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "restore-cache-evict", "150" }
            });

            Assert.That(() => _ = options.RestoreCacheEvict, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        [Category("Options")]
        public void BackupTestPercentageDefaultsToPointOne()
        {
            var options = CreateOptions();

            Assert.That(options.BackupTestPercentage, Is.EqualTo(0.1m));
        }

        [Test]
        [Category("Options")]
        public void BackupTestPercentageThrowsOnInvalidNumber()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "backup-test-percentage", "not-a-number" }
            });

            Assert.That(() => _ = options.BackupTestPercentage, Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [Category("Options")]
        public void BackupTestPercentageThrowsWhenOutOfRange()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "backup-test-percentage", "150" }
            });

            Assert.That(() => _ = options.BackupTestPercentage, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        [Category("Options")]
        public void BackupTestSampleCountIsClampedToZero()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "backup-test-samples", "-5" }
            });

            Assert.That(options.BackupTestSampleCount, Is.EqualTo(0));
        }

        [Test]
        [Category("Options")]
        public void IgnoreFilenamesDefaultsToCachedirTag()
        {
            var options = CreateOptions();

            Assert.That(options.IgnoreFilenames, Is.EquivalentTo(new[] { "CACHEDIR.TAG" }));
        }

        [Test]
        [Category("Options")]
        public void IndexfilePolicyReflectsProvidedValue()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "index-file-policy", "lookup" }
            });

            Assert.That(options.IndexfilePolicy, Is.EqualTo(Options.IndexFileStrategy.Lookup));
        }

        [Test]
        [Category("Options")]
        public void RestoreVolumeCacheHintDefaultsToUnlimited()
        {
            var options = CreateOptions();

            Assert.That(options.RestoreVolumeCacheHint, Is.EqualTo(-1L));
        }

        [Test]
        [Category("Options")]
        public void RestoreVolumeCacheMinFreeDefaultsToOneGigabyte()
        {
            var options = CreateOptions();

            Assert.That(options.RestoreVolumeCacheMinFree, Is.EqualTo(1L * 1024 * 1024 * 1024));
        }

        [Test]
        [Category("Options")]
        public void RestoreVolumeCacheMinFreeBareNumberTreatedAsGigabytes()
        {
            var options = CreateOptions(new Dictionary<string, string?>
            {
                { "restore-volume-cache-min-free", "2" }
            });

            Assert.That(options.RestoreVolumeCacheMinFree, Is.EqualTo(2L * 1024 * 1024 * 1024));
        }
    }
}
