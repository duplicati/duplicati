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
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The app execution aliases in "%LOCALAPPDATA%\Microsoft\WindowsApps" look like empty files, but cannot
/// be opened, so a backup gave a warning for each of them (#3178)
/// </summary>
[SupportedOSPlatform("windows")]
public class AppExecutionAliasTests : BasicSetupHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

    /// <summary>
    /// Makes an app execution alias, the way Windows does
    /// </summary>
    private static void CreateAlias(string path)
    {
        File.WriteAllBytes(path, []);

        // REPARSE_DATA_BUFFER, with a version and the package, the entry point, the program and the type of the app
        var data = BitConverter.GetBytes(3)
            .Concat(Encoding.Unicode.GetBytes("Test.App_0000000000000\0Test.App_0000000000000!App\0C:\\missing\\app.exe\01\0"))
            .ToArray();
        var buffer = BitConverter.GetBytes(0x8000001Bu)
            .Concat(BitConverter.GetBytes((ushort)data.Length))
            .Concat(new byte[2])
            .Concat(data)
            .ToArray();

        // GENERIC_WRITE, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT and FSCTL_SET_REPARSE_POINT
        using var handle = CreateFileW(path, 0x40000000, 0, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            Assert.Ignore($"An app execution alias cannot be made here: {new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message}");
    }

    [Test]
    [Category("Border")]
    [TestCase("store")]
    [TestCase("follow")]
    [TestCase("ignore")]
    public async Task AnAppExecutionAliasIsLeftOutWithoutAWarning(string policy)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("App execution aliases only exist on Windows");

        File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "contents");
        CreateAlias(Path.Combine(DATAFOLDER, "app.exe"));

        var options = TestOptions;
        options["symlink-policy"] = policy;
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            Assert.That((await c.ListAsync("*")).Files.Select(x => Path.GetFileName(x.Path.TrimEnd(Path.DirectorySeparatorChar))),
                Is.EquivalentTo(new[] { Path.GetFileName(DATAFOLDER), "file.txt" }));
    }
}
