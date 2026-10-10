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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Encryption;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// GPG encryption with a stand-in for the gpg program. The stand-in ignores its arguments, drops the
/// passphrase line from its input and writes the rest back, so it can be told to be slow, to write
/// a lot of error output, or to fail, which the real gpg does on large volumes and busy disks.
/// </summary>
[Category("Encryption")]
public class GPGEncryptionTests
{
    private string m_folder = "";

    [SetUp]
    public void SetUp()
    {
        m_folder = Path.Combine(Path.GetTempPath(), "duplicati-gpg-" + Path.GetRandomFileName());
        Directory.CreateDirectory(m_folder);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_folder, true); }
        catch { }
    }

    /// <summary>
    /// What the stand-in for gpg does
    /// </summary>
    private sealed record FakeGpg
    {
        public int DelayMilliseconds { get; init; }
        public int ErrorOutputBytes { get; init; }
        public bool ReadInput { get; init; } = true;
        public string? ErrorMessage { get; init; }
        public int ExitCode { get; init; }
        public string? PidFile { get; init; }
    }

    /// <summary>
    /// Writes the stand-in for gpg as a script and returns its path
    /// </summary>
    private string CreateFakeGpg(FakeGpg fake)
    {
        if (OperatingSystem.IsWindows())
        {
            var ps = new StringBuilder();
            if (fake.PidFile != null)
                ps.Append($"[IO.File]::WriteAllText('{fake.PidFile}', $PID); ");
            if (fake.ErrorOutputBytes > 0)
                ps.Append($"[Console]::Error.Write('x' * {fake.ErrorOutputBytes}); [Console]::Error.Flush(); ");
            if (fake.ReadInput)
                ps.Append("$m = New-Object IO.MemoryStream; [Console]::OpenStandardInput().CopyTo($m); $b = $m.ToArray(); $n = [Array]::IndexOf($b, [byte]10) + 1; ");
            ps.Append($"Start-Sleep -Milliseconds {fake.DelayMilliseconds}; ");
            if (fake.ErrorMessage != null)
                ps.Append($"[Console]::Error.WriteLine('{fake.ErrorMessage}'); ");
            if (fake.ReadInput)
                ps.Append("$o = [Console]::OpenStandardOutput(); $o.Write($b, $n, $b.Length - $n); $o.Flush(); ");
            ps.Append($"exit {fake.ExitCode}");

            var path = Path.Combine(m_folder, "fake-gpg.cmd");
            File.WriteAllText(path, $"@powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{ps}\"\r\n");
            return path;
        }
        else
        {
            var sh = new StringBuilder("#!/bin/sh\n");
            if (fake.PidFile != null)
                sh.Append($"echo $$ > '{fake.PidFile}'\n");
            if (fake.ErrorOutputBytes > 0)
                sh.Append($"head -c {fake.ErrorOutputBytes} /dev/zero | tr '\\0' x >&2\n");
            if (fake.ReadInput)
                sh.Append("t=$(mktemp)\ntail -n +2 > \"$t\"\n");
            sh.Append($"sleep {fake.DelayMilliseconds / 1000.0:0.###}\n".Replace(',', '.'));
            if (fake.ErrorMessage != null)
                sh.Append($"echo '{fake.ErrorMessage}' >&2\n");
            if (fake.ReadInput)
                sh.Append("cat \"$t\"\nrm -f \"$t\"\n");
            sh.Append($"exit {fake.ExitCode}\n");

            var path = Path.Combine(m_folder, "fake-gpg.sh");
            File.WriteAllText(path, sh.ToString());
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
    }

    private static GPGEncryption CreateGpg(string program)
        => new GPGEncryption("passphrase", new Dictionary<string, string> { [GPGEncryption.COMMANDLINE_OPTIONS_PATH] = program });

    private static byte[] RandomData(int size)
    {
        var data = new byte[size];
        new Random(42).NextBytes(data);
        return data;
    }

    /// <summary>
    /// An output stream that records writes made after it was closed, instead of throwing
    /// </summary>
    private sealed class TrackingStream : Stream
    {
        private readonly MemoryStream m_data = new();
        private volatile bool m_closed;
        private volatile bool m_writtenAfterClose;

        public byte[] Data => m_data.ToArray();
        public bool WrittenAfterClose => m_writtenAfterClose;

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (m_closed)
            {
                m_writtenAfterClose = true;
                return;
            }
            m_data.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            m_closed = true;
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>
    /// An output stream that fails every write, as a full disk does
    /// </summary>
    private sealed class FailingStream : Stream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("disk full");

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [Test]
    public void EncryptAndDecryptRoundTrip()
    {
        var gpg = CreateGpg(CreateFakeGpg(new FakeGpg()));
        var data = RandomData(1024 * 1024);

        using var encrypted = new MemoryStream();
        gpg.Encrypt(new MemoryStream(data), encrypted);
        using var decrypted = new MemoryStream();
        gpg.Decrypt(new MemoryStream(encrypted.ToArray()), decrypted);

        Assert.That(decrypted.ToArray(), Is.EqualTo(data));
    }

    /// <summary>
    /// gpg only finishes the volume after it has read all of it, which took longer than the five seconds
    /// Duplicati waited (#2566). The copy thread then wrote into the output after the caller had closed it,
    /// which crashed the process (#2443).
    /// </summary>
    [Test]
    public void EncryptWaitsForSlowGpg()
    {
        var gpg = CreateGpg(CreateFakeGpg(new FakeGpg { DelayMilliseconds = 7000 }));
        var data = RandomData(64 * 1024);

        Exception? error = null;
        var output = new TrackingStream();
        try
        {
            using (output)
                gpg.Encrypt(new MemoryStream(data), output);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        // Give a copy that outlived the call the time to show a late write
        if (error != null)
            SpinWait.SpinUntil(() => output.WrittenAfterClose, TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(output.WrittenAfterClose, Is.False);
            Assert.That(output.Data, Is.EqualTo(data));
        });
    }

    [Test]
    public void DecryptWaitsForSlowGpg()
    {
        var gpg = CreateGpg(CreateFakeGpg(new FakeGpg { DelayMilliseconds = 7000 }));
        var data = RandomData(64 * 1024);

        using var output = new MemoryStream();
        gpg.Decrypt(new MemoryStream(data), output);

        Assert.That(output.ToArray(), Is.EqualTo(data));
    }

    /// <summary>
    /// gpg writes warnings to its error output. Nothing read it until gpg had finished, so gpg stopped
    /// once the pipe was full, and stopped reading its input
    /// </summary>
    [Test]
    public void EncryptDoesNotStallOnErrorOutput()
    {
        var gpg = CreateGpg(CreateFakeGpg(new FakeGpg { ErrorOutputBytes = 1024 * 1024 }));
        var data = RandomData(1024 * 1024);

        using var output = new MemoryStream();
        var encrypt = Task.Run(() => gpg.Encrypt(new MemoryStream(data), output));

        Assert.That(encrypt.Wait(TimeSpan.FromSeconds(60)), Is.True, "Encryption did not finish");
        Assert.That(output.ToArray(), Is.EqualTo(data));
    }

    /// <summary>
    /// gpg stops reading its input when decryption fails. The thread that fed it then failed outside any handler
    /// </summary>
    [Test]
    public void DecryptReportsGpgFailure()
    {
        var gpg = CreateGpg(CreateFakeGpg(new FakeGpg
        {
            ReadInput = false,
            DelayMilliseconds = 2000,
            ErrorMessage = "gpg: decryption failed: Bad session key",
            ExitCode = 2
        }));
        var data = RandomData(1024 * 1024);

        using var output = new MemoryStream();
        var ex = Assert.Throws<CryptographicException>(() => gpg.Decrypt(new MemoryStream(data), output));

        Assert.That(ex!.Message, Does.Contain("decryption failed: Bad session key"));
    }

    /// <summary>
    /// When the output cannot be written, the error reaches the caller and gpg, which nobody reads from any more, is stopped
    /// </summary>
    [Test]
    public void EncryptReportsOutputFailureAndStopsGpg()
    {
        var pidFile = Path.Combine(m_folder, "pid");
        var gpg = CreateGpg(CreateFakeGpg(new FakeGpg { PidFile = pidFile }));
        var data = RandomData(1024 * 1024);

        var ex = Assert.Throws<IOException>(() => gpg.Encrypt(new MemoryStream(data), new FailingStream()));
        Assert.That(ex!.Message, Is.EqualTo("disk full"));

        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        Assert.That(SpinWait.SpinUntil(() => HasExited(pid), TimeSpan.FromSeconds(10)), Is.True, "gpg is still running");
    }

    private static bool HasExited(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
