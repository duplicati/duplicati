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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// On Linux and macOS the backup leaves out every entry that is not a regular file, a folder or
/// a symlink: devices, FIFOs and sockets. It logged all of them as block devices.
/// </summary>
[NonParallelizable]
public class SpecialFileExclusionLogTests : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public async Task SpecialFilesAreLoggedAsSpecialFilesAsync()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("FIFOs and sockets are not made on Windows");

        File.WriteAllText(Path.Combine(DATAFOLDER, "regular.txt"), "a");
        var fifo = Path.Combine(DATAFOLDER, "a-fifo");
        Process.Start("mkfifo", fifo)!.WaitForExit();

        // A socket path is limited to about 100 characters (108 on Linux, 104 on macOS), and the
        // data folder can be deeper than that, so the socket is made in a folder of its own in
        // /tmp, which is given as another source. Path.GetTempPath() follows TMPDIR, which is set
        // to the temporary folder of the operation when a database is opened, and that can be
        // deep too. /tmp is a link to /private/tmp on macOS, so the link is resolved.
        var tmp = new DirectoryInfo("/tmp");
        var socketFolder = Path.Combine(tmp.ResolveLinkTarget(true)?.FullName ?? tmp.FullName, "dup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(socketFolder);
        try
        {
            var socket = Path.Combine(socketFolder, "a-socket");
            // Kept open until the backup is done: disposing the socket removes its file
            using (var listener = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified))
            {
                listener.Bind(new System.Net.Sockets.UnixDomainSocketEndPoint(socket));
                // File.Exists is false for a FIFO or a socket, so look for them in the folder listing
                Assert.That(Directory.GetFileSystemEntries(DATAFOLDER), Does.Contain(fifo), "The FIFO should exist");
                Assert.That(Directory.GetFileSystemEntries(socketFolder), Does.Contain(socket), "The socket should exist");

                var logfile = Path.Combine(BASEFOLDER, "special-files.log");
                // The log file is appended to, and the base folder is not always cleared between runs
                File.Delete(logfile);
                var options = new Dictionary<string, string>(TestOptions)
                {
                    ["log-file"] = logfile,
                    ["log-file-log-level"] = "verbose",
                };

                using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                {
                    // A character device given as a source of its own
                    var result = await c.BackupAsync(new[] { DATAFOLDER, socketFolder, "/dev/null" });
                    Assert.That(result.ExaminedFiles, Is.EqualTo(1), "Only the regular file should be backed up");
                }

                var lines = File.ReadAllLines(logfile);
                foreach (var path in new[] { fifo, socket, "/dev/null" })
                    Assert.That(lines.Any(l => l.Contains("-ExcludingSpecialFile]") && l.Contains("Excluding special file (device, FIFO or socket): " + path)), Is.True,
                        $"{path} should be logged as a special file");
                Assert.That(lines.Where(l => l.Contains("block device", StringComparison.OrdinalIgnoreCase) || l.Contains("BlockDevice")), Is.Empty,
                    "Nothing here is a block device");
            }
        }
        finally
        {
            try { Directory.Delete(socketFolder, true); } catch { }
        }
    }
}
