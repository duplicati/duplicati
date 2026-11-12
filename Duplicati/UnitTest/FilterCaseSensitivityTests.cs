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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The case-sensitivity of filters is chosen per operation with the "filter-case-sensitivity"
/// option, and a source provider that names its items with case-sensitive paths makes the
/// filters case-sensitive, so a filter selects one of two paths that differ only by casing.
/// </summary>
[NonParallelizable]
public class FilterCaseSensitivityTests : BasicSetupHelper
{
    private const string OPTION = "filter-case-sensitivity";

    /// <summary>
    /// A source entry, made as a proxy so it does not name the members of the interface
    /// </summary>
    public class Entry : DispatchProxy
    {
        public string EntryPath = "";
        public bool Folder;
        public bool Root;
        public List<ISourceProviderEntry> Children = new();

        public static ISourceProviderEntry Create(string path, bool folder, bool root, params ISourceProviderEntry[] children)
        {
            var proxy = Create<ISourceProviderEntry, Entry>();
            var e = (Entry)(object)proxy;
            e.EntryPath = path;
            e.Folder = folder;
            e.Root = root;
            e.Children.AddRange(children);
            return proxy;
        }

        private async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken token)
        {
            await Task.CompletedTask;
            foreach (var c in Children)
                yield return c;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_Path": return EntryPath;
                case "get_IsFolder": return Folder;
                case "get_IsRootEntry": return Root;
                case "get_Attributes": return Folder ? FileAttributes.Directory : FileAttributes.Normal;
                case "get_Size": return Folder ? 0L : (long)EntryPath.Length;
                case "get_CreatedUtc":
                case "get_LastModificationUtc": return new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                case "Enumerate": return EnumerateAsync((CancellationToken)args![0]!);
                case "OpenRead": return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(EntryPath)));
                case "GetMinorMetadata": return Task.FromResult(new Dictionary<string, string?>());
                case "FileExists": return Task.FromResult(false);
            }
            var type = targetMethod.ReturnType;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    /// <summary>
    /// A provider that lists two files whose paths differ only by casing
    /// </summary>
    private sealed class CaseTwinProvider : ISourceProviderModule
    {
        private readonly string _mountPoint;

        public CaseTwinProvider() { _mountPoint = string.Empty; }

        public CaseTwinProvider(string url, string mountPoint, Dictionary<string, string?> options) { _mountPoint = mountPoint; }

        public string Key => "test-case-twins";
        public string DisplayName => "Test case twin provider";
        public string Description => "Test provider with paths that differ only by casing";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public string MountedPath => _mountPoint;
        public bool NeedsStoredMetadata => false;
        public bool NeedsCaseSensitiveFilters => true;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return Entry.Create(_mountPoint, true, true,
                Entry.Create(_mountPoint + "AbCdEf", false, false),
                Entry.Create(_mountPoint + "aBcDeF", false, false),
                Entry.Create(_mountPoint + "other", false, false));
        }

        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
            => Task.FromResult<ISourceProviderEntry?>(null);

        public void Dispose() { }
    }

    private const string TwinSource = "@/twins|test-case-twins://source";

    private static bool Match(IFilter filter, string path)
        => filter.Matches(path, out var result, out _) && result;

    [Test]
    [Category("Filter")]
    public void ExplicitCaseSensitivityDecidesTheMatch()
    {
        foreach (var pattern in new[] { "/data/File.txt", "/data/F*.txt", "[/data/F.*\\.txt]" })
        {
            var sensitive = new FilterExpression(pattern, true, true);
            Assert.That(sensitive.CaseSensitive, Is.True);
            Assert.That(Match(sensitive, "/data/File.txt"), Is.True, pattern);
            Assert.That(Match(sensitive, "/data/file.txt"), Is.False, pattern);

            var insensitive = new FilterExpression(pattern, true, false);
            Assert.That(insensitive.CaseSensitive, Is.False);
            Assert.That(Match(insensitive, "/data/File.txt"), Is.True, pattern);
            Assert.That(Match(insensitive, "/data/file.txt"), Is.True, pattern);
        }
    }

    [Test]
    [Category("Filter")]
    public void ChangingTheCaseSensitivityKeepsTheWrittenCasing()
    {
        // A wildcard filter that ignores case is matched in upper case,
        // which must not leak into the filter when it is made case-sensitive
        var insensitive = new FilterExpression("/data/Fi*.txt", true, false);
        Assert.That(insensitive.Serialize(), Is.EqualTo(new[] { "+/data/Fi*.txt" }));

        var sensitive = FilterExpression.WithCaseSensitivity(insensitive, true);
        Assert.That(Match(sensitive, "/data/File.txt"), Is.True);
        Assert.That(Match(sensitive, "/data/FILE.TXT"), Is.False);

        Assert.That(FilterExpression.WithCaseSensitivity(sensitive, true), Is.SameAs(sensitive));

        var joined = JoinedFilterExpression.Join(new FilterExpression("/a/Ex*", false, false), new FilterExpression("/a/In*", true, false));
        var converted = FilterExpression.WithCaseSensitivity(joined, true);
        Assert.That(Match(converted, "/a/Inside"), Is.True);
        Assert.That(converted.Matches("/a/inside", out _, out _), Is.False);
        Assert.That(converted.Matches("/a/Excluded", out var excluded, out _) && !excluded, Is.True);
        Assert.That(converted.Matches("/a/excluded", out _, out _), Is.False);
    }

    [Test]
    [Category("Filter")]
    public async Task ScopeSetsTheCaseSensitivityOfNewFiltersAsync()
    {
        var before = FilterExpression.DefaultCaseSensitive;
        Assert.That(before, Is.EqualTo(Library.Utility.Utility.IsFSCaseSensitive));

        foreach (var value in new[] { true, false })
        {
            using (FilterExpression.StartCaseSensitivityScope(value))
            {
                Assert.That(new FilterExpression("/data/F*").CaseSensitive, Is.EqualTo(value));

                // The setting follows the operation into the code it awaits and starts
                await Task.Yield();
                Assert.That(await Task.Run(() => new FilterExpression("/data/F*").CaseSensitive), Is.EqualTo(value));

                // A log filter matches log tags, and keeps following the filesystem
                var log = FilterExpression.ParseLogFilter("+*-Tag");
                Assert.That(((FilterExpression)log).CaseSensitive, Is.EqualTo(Library.Utility.Utility.IsFSCaseSensitive));
            }

            Assert.That(FilterExpression.DefaultCaseSensitive, Is.EqualTo(before));
        }
    }

    [Test]
    [Category("Filter")]
    public void OptionChoosesTheCaseSensitivity()
    {
        Assert.That(new Options(new Dictionary<string, string?>()).FilterCaseSensitive, Is.EqualTo(Library.Utility.Utility.IsFSCaseSensitive));
        Assert.That(new Options(new Dictionary<string, string?> { [OPTION] = "default" }).FilterCaseSensitive, Is.EqualTo(Library.Utility.Utility.IsFSCaseSensitive));
        Assert.That(new Options(new Dictionary<string, string?> { [OPTION] = "sensitive" }).FilterCaseSensitive, Is.True);
        Assert.That(new Options(new Dictionary<string, string?> { [OPTION] = "Insensitive" }).FilterCaseSensitive, Is.False);
    }

    [Test]
    [Category("Filter")]
    public void SourceProviderMakesFiltersCaseSensitiveUnlessTheOptionIsSet()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new CaseTwinProvider());

        var options = new Dictionary<string, string?>();
        Assert.That(SourceProviderFactory.EnableCaseSensitiveFiltersIfRequiredBySources(["/localfolder/", TwinSource], options), Is.True);
        Assert.That(options[OPTION], Is.EqualTo("sensitive"));

        options = new Dictionary<string, string?> { [OPTION] = "insensitive" };
        Assert.That(SourceProviderFactory.EnableCaseSensitiveFiltersIfRequiredBySources([TwinSource], options), Is.False);
        Assert.That(options[OPTION], Is.EqualTo("insensitive"));

        options = new Dictionary<string, string?>();
        Assert.That(SourceProviderFactory.EnableCaseSensitiveFiltersIfRequiredBySources(["/localfolder/"], options), Is.False);
        Assert.That(options.ContainsKey(OPTION), Is.False);
    }

    private async Task<string[]> ListAsync(Dictionary<string, string> options, string filter)
    {
        using var c = new Controller("file://" + TARGETFOLDER, options.Expand(new { version = 0 }), null);
        var r = await c.ListAsync(filter);
        TestUtils.AssertResults(r);
        return r.Files.Select(x => x.Path).Where(x => !x.EndsWith(Path.DirectorySeparatorChar)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    [Test]
    [Category("Filter")]
    public async Task FiltersSelectOneOfTwoPathsThatDifferOnlyByCasingAsync()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new CaseTwinProvider());

        var options = new Dictionary<string, string>(TestOptions) { ["no-encryption"] = "true" };

        // The backup makes the filters case-sensitive on its own, and keeps both files
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([TwinSource]));

        var all = await ListAsync(options, "*");
        var upper = all.Single(x => x.EndsWith("AbCdEf", StringComparison.Ordinal));
        var lower = all.Single(x => x.EndsWith("aBcDeF", StringComparison.Ordinal));
        Assert.That(all, Has.Length.EqualTo(3));

        // The other operations have no source to read the setting from, so they are given the option
        var sensitive = new Dictionary<string, string>(options) { [OPTION] = "sensitive" };
        var insensitive = new Dictionary<string, string>(options) { [OPTION] = "insensitive" };

        // An exact path
        Assert.That(await ListAsync(sensitive, upper), Is.EqualTo(new[] { upper }));
        Assert.That(await ListAsync(sensitive, lower), Is.EqualTo(new[] { lower }));

        // A wildcard and a regular expression
        Assert.That(await ListAsync(sensitive, "*AbCd*"), Is.EqualTo(new[] { upper }));
        Assert.That(await ListAsync(sensitive, "[.*aBcD.*]"), Is.EqualTo(new[] { lower }));
        Assert.That(await ListAsync(insensitive, "*AbCd*"), Is.EquivalentTo(new[] { upper, lower }));
        Assert.That(await ListAsync(insensitive, "[.*aBcD.*]"), Is.EquivalentTo(new[] { upper, lower }));

        // Purging one of the files leaves the other in the backup
        using (var c = new Controller("file://" + TARGETFOLDER, sensitive, null))
        {
            var res = await c.PurgeFilesAsync(new FilterExpression(upper));
            TestUtils.AssertResults(res);
            Assert.That(res.RemovedFileCount, Is.EqualTo(1));
        }

        var remaining = await ListAsync(options, "*");
        Assert.That(remaining, Does.Contain(lower));
        Assert.That(remaining, Does.Not.Contain(upper));
        Assert.That(remaining, Has.Length.EqualTo(2));

        // Without case-sensitivity, a wildcard for the purged file takes the other one too
        using (var c = new Controller("file://" + TARGETFOLDER, insensitive, null))
        {
            var res = await c.PurgeFilesAsync(new FilterExpression("*AbCdEf"));
            TestUtils.AssertResults(res);
            Assert.That(res.RemovedFileCount, Is.EqualTo(1));
        }

        Assert.That(await ListAsync(options, "*"), Has.Length.EqualTo(1));
    }
}
