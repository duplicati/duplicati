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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Without --overwrite, a restore to the original location leaves a file that differs from
/// the backed-up one as it is, and puts the backed-up version beside it under a new name.
/// A file that was appended to after the backup starts with the backed-up content, so every
/// block matches, and the restore took it for the backed-up file and cut it back to the
/// backed-up length, losing what was appended.
/// </summary>
public class RestoreNoOverwriteAppendedTests : BasicSetupHelper
{
    /// <summary>The file that is backed up and restored</summary>
    private string TargetFile => Path.Combine(this.DATAFOLDER, "file.bin");

    /// <summary>
    /// Makes some bytes that do not repeat
    /// </summary>
    /// <param name="length">The number of bytes</param>
    /// <param name="seed">What to make them from</param>
    /// <returns>The bytes.</returns>
    private static byte[] Bytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// Backs up the file with the first content, gives it the second, and restores it to
    /// the original location
    /// </summary>
    /// <param name="backedUp">The content at backup time</param>
    /// <param name="atRestore">The content at restore time</param>
    /// <param name="extraOptions">Options to set for the restore, on top of the test options</param>
    /// <returns>The content of every file in the data folder after the restore, by name.</returns>
    private async Task<Dictionary<string, byte[]>> BackupChangeAndRestoreAsync(byte[] backedUp, byte[] atRestore, Dictionary<string, string> extraOptions)
    {
        File.WriteAllBytes(TargetFile, backedUp);

        var options = new Dictionary<string, string>(this.TestOptions);
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        File.WriteAllBytes(TargetFile, atRestore);

        foreach (var kv in extraOptions)
            options[kv.Key] = kv.Value;
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RestoreAsync(null));

        return Directory.EnumerateFiles(this.DATAFOLDER)
            .ToDictionary(x => Path.GetFileName(x), File.ReadAllBytes);
    }

    /// <summary>
    /// Checks that the file is left as it was, and that the backed-up version is beside it
    /// </summary>
    /// <param name="files">The files after the restore, by name</param>
    /// <param name="backedUp">The content at backup time</param>
    /// <param name="atRestore">The content at restore time</param>
    private static void AssertKeptAndRestoredBeside(Dictionary<string, byte[]> files, byte[] backedUp, byte[] atRestore)
    {
        var names = string.Join(", ", files.Select(x => $"{x.Key} ({x.Value.Length} bytes)"));

        Assert.That(files["file.bin"], Is.EqualTo(atRestore),
            $"the file that was there should be left as it was; got: {names}");

        var beside = files.Where(x => x.Key != "file.bin").ToArray();
        Assert.That(beside, Has.Length.EqualTo(1), $"the backed-up version should be restored beside it; got: {names}");
        Assert.That(beside[0].Value, Is.EqualTo(backedUp), $"the file beside it should be the backed-up version; got: {names}");
    }

    /// <summary>
    /// The case from the report: a small file, one block, with text added at the end
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AnAppendedFileIsNotCutBackWithoutOverwrite()
    {
        var backedUp = "kept"u8.ToArray();
        var atRestore = "kept-changed"u8.ToArray();

        var files = await BackupChangeAndRestoreAsync(backedUp, atRestore, new() { ["overwrite"] = "false" });

        AssertKeptAndRestoredBeside(files, backedUp, atRestore);
    }

    /// <summary>
    /// The same over several blocks, with a partial last block, as a growing log would be
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AnAppendedFileOfSeveralBlocksIsNotCutBackWithoutOverwrite()
    {
        var backedUp = Bytes(25 * 1024, 1);
        byte[] atRestore = [.. backedUp, .. Bytes(5 * 1024, 2)];

        var files = await BackupChangeAndRestoreAsync(backedUp, atRestore, new() { ["overwrite"] = "false" });

        AssertKeptAndRestoredBeside(files, backedUp, atRestore);
    }

    /// <summary>
    /// With the local blocks in use, the blocks that match are copied from the file that is
    /// there, and the copy reads whole blocks, so the version beside it must not pick up
    /// what was appended
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AnAppendedFileIsNotCutBackWithoutOverwriteWhenUsingLocalBlocks()
    {
        var backedUp = Bytes(25 * 1024, 1);
        byte[] atRestore = [.. backedUp, .. Bytes(5 * 1024, 2)];

        var files = await BackupChangeAndRestoreAsync(backedUp, atRestore,
            new() { ["overwrite"] = "false", ["restore-with-local-blocks"] = "true" });

        AssertKeptAndRestoredBeside(files, backedUp, atRestore);
    }

    /// <summary>
    /// An empty file has no blocks, so every block of it matches whatever is there now
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AFileThatWasEmptyIsNotCutBackWithoutOverwrite()
    {
        var backedUp = Array.Empty<byte>();
        var atRestore = "written after the backup"u8.ToArray();

        var files = await BackupChangeAndRestoreAsync(backedUp, atRestore, new() { ["overwrite"] = "false" });

        AssertKeptAndRestoredBeside(files, backedUp, atRestore);
    }

    /// <summary>
    /// Green before and after: with --overwrite, going back to the backed-up version is what
    /// is asked for, so the file is cut back
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AnAppendedFileIsCutBackWithOverwrite()
    {
        var backedUp = Bytes(25 * 1024, 1);
        byte[] atRestore = [.. backedUp, .. Bytes(5 * 1024, 2)];

        var files = await BackupChangeAndRestoreAsync(backedUp, atRestore, new() { ["overwrite"] = "true" });

        Assert.That(files.Keys, Is.EquivalentTo(new[] { "file.bin" }));
        Assert.That(files["file.bin"], Is.EqualTo(backedUp));
    }

    /// <summary>
    /// Green before and after: a file that differs other than at the end was already kept,
    /// with the backed-up version beside it
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AFileChangedInTheMiddleIsKeptWithoutOverwrite()
    {
        var backedUp = Bytes(25 * 1024, 1);
        var atRestore = backedUp.ToArray();
        atRestore[12 * 1024] ^= 0xff;

        var files = await BackupChangeAndRestoreAsync(backedUp, atRestore, new() { ["overwrite"] = "false" });

        AssertKeptAndRestoredBeside(files, backedUp, atRestore);
    }
}
