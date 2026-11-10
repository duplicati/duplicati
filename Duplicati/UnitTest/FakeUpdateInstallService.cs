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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.UnitTest;

/// <summary>
/// An update install service that records the console requests instead of checking for or installing updates
/// </summary>
internal sealed class FakeUpdateInstallService : IUpdateInstallService
{
    /// <summary>
    /// The support verdict reported to the remote controller
    /// </summary>
    public UpdateInstallSupport Support { get; set; } = new UpdateInstallSupport(true, null, "zip");

    /// <summary>
    /// The outcome returned for a console request; null makes the request throw <see cref="RequestError"/>
    /// </summary>
    public ConsoleUpdateInstallResult? Result { get; set; } = new ConsoleUpdateInstallResult(ConsoleUpdateInstallStatus.Installing, "2.3.0.0");

    /// <summary>
    /// The exception thrown when <see cref="Result"/> is null
    /// </summary>
    public Exception RequestError { get; set; } = new InvalidOperationException("No update can be installed");

    /// <summary>
    /// The number of console requests received
    /// </summary>
    public int ConsoleRequests { get; private set; }

    public HostEnvironment Host => throw new NotSupportedException();
    public bool InProgress => false;
    public void BeginInstall() => throw new NotSupportedException();
    public void CancelInstall() => throw new NotSupportedException();
    public void CompletePendingInstall() => throw new NotSupportedException();

    public Task<ConsoleUpdateInstallResult> InstallLatestForConsoleAsync(CancellationToken cancellationToken)
    {
        ConsoleRequests++;
        return Result == null
            ? Task.FromException<ConsoleUpdateInstallResult>(RequestError)
            : Task.FromResult(Result);
    }
}
