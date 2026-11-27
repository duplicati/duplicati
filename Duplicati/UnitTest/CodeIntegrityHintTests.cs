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
using System.IO;
using Duplicati.Library.Crashlog;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Checks the explanation for a crash caused by Windows code integrity
/// (Smart App Control or App Control for Business) blocking a library.
/// The exceptions are built the way .NET reports a blocked load.
/// </summary>
public class CodeIntegrityHintTests : BasicSetupHelper
{
    /// <summary>
    /// An exception that only carries the ERROR_SYSTEM_INTEGRITY_POLICY HRESULT
    /// </summary>
    private sealed class BlockedByPolicyException : Exception
    {
        public BlockedByPolicyException() : base("Blocked")
            => HResult = unchecked((int)0x800711C7);
    }

    /// <summary>
    /// A blocked managed assembly, as Microsoft.Data.Sqlite reports it on the first connection
    /// </summary>
    private static Exception BlockedAssembly()
        => new TypeInitializationException("Microsoft.Data.Sqlite.SqliteConnection",
            new FileLoadException(
                "Could not load file or assembly 'SQLitePCLRaw.batteries_v2, Version=3.0.5.0, Culture=neutral, PublicKeyToken=null'. An Application Control policy has blocked this file. (0x800711C7)",
                "SQLitePCLRaw.batteries_v2, Version=3.0.5.0, Culture=neutral, PublicKeyToken=null"));

    /// <summary>
    /// Creates an empty folder to act as the install folder
    /// </summary>
    private string CreateInstallFolder(bool withCatalog)
    {
        var folder = Path.Combine(DATAFOLDER, "install");
        Directory.CreateDirectory(folder);
        if (withCatalog)
            File.WriteAllBytes(Path.Combine(folder, "Duplicati.cat"), [0x30]);
        return folder;
    }

    /// <summary>
    /// Creates the hint, failing the test if the crash is not recognized
    /// </summary>
    private static string RequireHint(Exception ex, string folder)
        => CodeIntegrityHint.Create(ex, folder)
            ?? throw new AssertionException("The crash was not recognized as a blocked library");

    [Test]
    [Category("Crashlog")]
    public void ABlockedAssemblyIsNamedWithTheRegistrationCommand()
    {
        var folder = CreateInstallFolder(true);
        var hint = RequireHint(BlockedAssembly(), folder);

        StringAssert.Contains("Windows blocked SQLitePCLRaw.batteries_v2.dll from loading", hint);
        StringAssert.Contains($"\"{Path.Combine(folder, "Duplicati.WindowsService.exe")}\" register-catalog", hint);
    }

    [Test]
    [Category("Crashlog")]
    public void ABlockedNativeLibraryInsideAnAggregateIsNamed()
    {
        var ex = new AggregateException(
            new InvalidOperationException("Unrelated"),
            new DllNotFoundException("Unable to load DLL 'e_sqlite3' or one of its dependencies: An Application Control policy has blocked this file. (0x800711C7)"));

        var hint = RequireHint(ex, CreateInstallFolder(true));

        StringAssert.Contains("Windows blocked e_sqlite3 from loading", hint);
    }

    [Test]
    [Category("Crashlog")]
    public void TheHResultAloneIsRecognized()
    {
        var hint = RequireHint(new InvalidOperationException("Outer", new BlockedByPolicyException()), CreateInstallFolder(true));

        StringAssert.Contains("a library that Duplicati needs", hint);
    }

    [Test]
    [Category("Crashlog")]
    public void ABuildWithoutACatalogDoesNotSuggestTheCommand()
    {
        var hint = RequireHint(BlockedAssembly(), CreateInstallFolder(false));

        StringAssert.Contains("has no signature catalog", hint);
        StringAssert.DoesNotContain("register-catalog", hint);
    }

    [Test]
    [Category("Crashlog")]
    public void OtherCrashesGetNoHint()
    {
        var ex = new TypeInitializationException("Some.Type", new FileLoadException("Could not load file or assembly 'Other'.", "Other"));

        Assert.IsNull(CodeIntegrityHint.Create(ex, CreateInstallFolder(true)));
    }
}
