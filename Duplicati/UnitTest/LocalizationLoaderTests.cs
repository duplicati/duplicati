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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Localization;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests that the culture discovery and the catalog loading of
    /// <see cref="MoLocalizationService"/> agree with each other. Discovery accepts both
    /// hyphenated and underscored culture segments in a filename, so every catalog that
    /// is advertised as a supported culture must also be loadable by the filename
    /// candidates of that culture — otherwise the language shows up in the picker but
    /// silently falls back to another catalog.
    /// </summary>
    [TestFixture]
    [Category("LocalizationLoader")]
    public class LocalizationLoaderTests
    {
        /// <summary>
        /// The same filename pattern the service uses for discovery.
        /// </summary>
        private static readonly Regex MoFileMatcher = new Regex(@"localization-(?<culture>" + LocalizationService.CI_MATCHER + @")\.mo");

        /// <summary>
        /// Lists the embedded catalog filenames (e.g. <c>localization-zh-Hans.mo</c>)
        /// together with the culture the discovery parses out of them.
        /// </summary>
        private static IEnumerable<(string Filename, CultureInfo Culture)> EmbeddedCatalogs()
        {
            foreach (var name in MoLocalizationService.SearchAssembly.GetManifestResourceNames())
            {
                var m = MoFileMatcher.Match(name);
                if (!m.Success)
                    continue;

                var ci = LocalizationService.ParseCulture(m.Groups["culture"].Value);
                if (ci != null)
                    yield return (m.Value, ci);
            }
        }

        /// <summary>
        /// Every culture that discovery reports must be able to load its own catalog
        /// file. A mismatch means the language is offered in the UI but the load falls
        /// through to a more generic catalog (or none), which is exactly what happened
        /// to zh-Hans: it was discovered from <c>localization-zh-Hans.mo</c>, but the
        /// loader only probed the underscored form and fell back to the generic
        /// <c>localization-zh.mo</c>.
        /// </summary>
        [Test]
        public void EveryEmbeddedCatalogIsLoadableByItsOwnCulture()
        {
            var catalogs = EmbeddedCatalogs().ToList();
            Assert.IsNotEmpty(catalogs, "The localization assembly should embed at least one catalog");

            var unloadable = catalogs
                .Where(x => !MoLocalizationService.GetCandidateFilenames(x.Culture).Contains(x.Filename))
                .Select(x => $"{x.Filename} (culture {x.Culture.Name})")
                .ToList();

            Assert.IsEmpty(unloadable,
                "Every discovered catalog must be loadable by its own culture, but these are not: " + string.Join(", ", unloadable));
        }

        /// <summary>
        /// A hyphenated culture must probe the catalog named exactly after it before any
        /// fallback, so zh-Hans loads Simplified Chinese and not the generic zh catalog.
        /// </summary>
        [Test]
        public void HyphenatedCultureProbesItsExactNameFirst()
        {
            var candidates = MoLocalizationService.GetCandidateFilenames(new CultureInfo("zh-Hans")).ToList();

            Assert.AreEqual("localization-zh-Hans.mo", candidates.First(),
                "The exact culture name must be probed first");
            Assert.Contains("localization-zh.mo", candidates,
                "The generic language catalog must remain as a fallback");
        }

        /// <summary>
        /// The legacy underscored filenames (as produced by older Transifex language
        /// codes, e.g. <c>localization-zh_CN.mo</c>) must keep resolving for their
        /// culture, which is parsed with a hyphen.
        /// </summary>
        [Test]
        public void UnderscoredLegacyNamesStillResolve()
        {
            var ci = LocalizationService.ParseCulture("zh_CN");
            Assert.IsNotNull(ci);
            Assert.AreEqual("zh-CN", ci.Name);

            Assert.Contains("localization-zh_CN.mo", MoLocalizationService.GetCandidateFilenames(ci).ToList(),
                "The underscored legacy filename must remain a candidate");
        }

        /// <summary>
        /// A culture without a region or script produces no duplicate probes.
        /// </summary>
        [Test]
        public void PlainLanguageCultureProducesSingleCandidate()
        {
            var candidates = MoLocalizationService.GetCandidateFilenames(new CultureInfo("de")).ToList();
            Assert.AreEqual(1, candidates.Count, $"Expected a single candidate, got: {string.Join(", ", candidates)}");
            Assert.AreEqual("localization-de.mo", candidates[0]);
        }
    }
}
