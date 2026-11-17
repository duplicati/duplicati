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

using System.Diagnostics;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Runs the script from the "post-queue-processing-script" server setting.
/// The script is started the same way as the scripts of a backup: the path is the program to start,
/// so on Windows it is an executable or a batch file.
/// </summary>
/// <param name="connection">The server database connection, which holds the setting</param>
/// <param name="settings">The timings, of which the script timeout is used</param>
public class PostQueueScriptRunner(Connection connection, QueueCompletionSettings settings) : IPostQueueScriptRunner
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<PostQueueScriptRunner>();

    /// <summary>
    /// The name of the environment variable that tells the script why it is run, as for the scripts of a backup
    /// </summary>
    public const string EventNameVariable = "DUPLICATI__EVENTNAME";

    /// <summary>
    /// The event name the script is run with
    /// </summary>
    public const string EventName = "QUEUE-COMPLETED";

    /// <summary>
    /// Gets the path from the setting, or <c>null</c> if no script is set
    /// </summary>
    private string? ScriptPath
    {
        get
        {
            var path = connection.ApplicationSettings.PostQueueProcessingScript;
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        }
    }

    /// <inheritdoc/>
    public bool CanRun()
    {
        var path = ScriptPath;
        return path != null && File.Exists(path);
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var path = ScriptPath ?? throw new InvalidOperationException("No script is set to run after the task queue");

        // The output is not captured: a script that shuts down the computer may never close its streams
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.EnvironmentVariables[EventNameVariable] = EventName;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start the script {path}");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(settings.ScriptTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The script is left running, as it was asked for and may still be doing its work
            Library.Logging.Log.WriteWarningMessage(LOGTAG, "PostQueueScriptTimeout", null, "The script {0} is still running after {1}, and is no longer waited for", path, settings.ScriptTimeout);
            return;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The script {path} failed with exit code {process.ExitCode}");
    }
}
