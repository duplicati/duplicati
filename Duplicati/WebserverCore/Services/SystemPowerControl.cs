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

using Duplicati.Library.Main.SystemPower;
using Duplicati.Server.Serialization;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Shuts down or restarts the computer through the interface of the operating system
/// </summary>
public class SystemPowerControl : ISystemPowerControl
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<SystemPowerControl>();

    /// <summary>
    /// The time the operating system waits before it shuts down, which leaves a person at the computer time to cancel.
    /// A macOS user session shuts down at once, as the login window has no delay.
    /// </summary>
    public const int DelayMinutes = 1;

    /// <summary>
    /// The message the operating system shows to the people logged in, where it supports one
    /// </summary>
    private const string Message = "Duplicati has completed the queued tasks";

    /// <inheritdoc/>
    public async Task<bool> CanPerformAsync(QueueCompletionAction action, CancellationToken cancellationToken)
    {
        if (action is not (QueueCompletionAction.Shutdown or QueueCompletionAction.Reboot))
            return false;

        var reboot = action == QueueCompletionAction.Reboot;
        try
        {
            if (OperatingSystem.IsWindows())
                return WindowsSystemPower.CanShutdown();
            if (OperatingSystem.IsMacOS())
                return MacOSSystemPower.CanPerform(Environment.IsPrivilegedProcess);
            if (OperatingSystem.IsLinux())
                return await LinuxSystemPower.CanPerformAsync(reboot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A computer without a login manager, such as a container, ends here
            Library.Logging.Log.WriteVerboseMessage(LOGTAG, "PowerCapabilityCheckFailed", ex, "Failed to check if the computer can {0}: {1}", action, ex.Message);
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task PerformAsync(QueueCompletionAction action, CancellationToken cancellationToken)
    {
        if (action is not (QueueCompletionAction.Shutdown or QueueCompletionAction.Reboot))
            throw new ArgumentOutOfRangeException(nameof(action), action, "The action must be a shutdown or a reboot");

        var reboot = action == QueueCompletionAction.Reboot;
        if (OperatingSystem.IsWindows())
            WindowsSystemPower.Perform(reboot, DelayMinutes * 60, Message);
        else if (OperatingSystem.IsMacOS())
            await MacOSSystemPower.PerformAsync(reboot, Environment.IsPrivilegedProcess, DelayMinutes, Message, cancellationToken).ConfigureAwait(false);
        else if (OperatingSystem.IsLinux())
            await LinuxSystemPower.PerformAsync(reboot, TimeSpan.FromMinutes(DelayMinutes), cancellationToken).ConfigureAwait(false);
        else
            throw new PlatformNotSupportedException("Shutting down the computer is not supported on this operating system");
    }
}
