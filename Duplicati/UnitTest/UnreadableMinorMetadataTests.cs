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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation.Backup;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The permissions of a file are read with the rest of its metadata, and some file systems cannot
/// give them (issue #3163). That must cost the permissions only, not the timestamps and attributes.
/// </summary>
[TestFixture]
public class UnreadableMinorMetadataTests
{
    private static readonly DateTime Modified = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime Created = new(2023, 6, 7, 8, 9, 10, DateTimeKind.Utc);

    /// <summary>
    /// A file whose permissions cannot be read
    /// </summary>
    public class Entry : DispatchProxy
    {
        public static ISourceProviderEntry Create() => Create<ISourceProviderEntry, Entry>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.Name switch
            {
                "get_Path" => @"C:\data\file.txt",
                "get_LastModificationUtc" => Modified,
                "get_CreatedUtc" => Created,
                "GetMinorMetadata" => Task.FromException<Dictionary<string, string?>>(
                    new UnauthorizedAccessException("Attempted to perform an unauthorized operation.")),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
    }

    [Test]
    [Category("Targeted")]
    public async Task TheTimestampsAndAttributesAreKeptWhenThePermissionsCannotBeReadAsync()
    {
        var metadata = await MetadataGenerator.GenerateMetadataAsync(
            Entry.Create(), FileAttributes.Archive | FileAttributes.ReadOnly, new Options(new Dictionary<string, string?>()), CancellationToken.None);

        Assert.That(metadata["CoreLastWritetime"], Is.EqualTo(Modified.Ticks.ToString()));
        Assert.That(metadata["CoreCreatetime"], Is.EqualTo(Created.Ticks.ToString()));
        Assert.That(metadata["CoreAttributes"], Is.EqualTo((FileAttributes.Archive | FileAttributes.ReadOnly).ToString()));
    }
}
