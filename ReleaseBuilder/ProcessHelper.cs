using System.Diagnostics;

namespace ReleaseBuilder;

/// <summary>
/// Helper methods for executing a commandline program
/// </summary>
public static class ProcessHelper
{
    /// <summary>
    /// Starts a commandline program and waits for it to complete
    /// </summary>
    /// <param name="command"></param>
    /// <param name="workingDirectory">The working directory to run in; <c>null</c> means current directory</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <param name="codeIsError">Callback method that is invoked with the error code from the process; the result indicates if the status code should be interpreted as an error.
    /// Default value is <c>null</c> which will treat anything non-zero as an error</param>
    /// <param name="suppressStdErr">If <c>true</c>, stderr is not forwarded to the console</param>
    /// <param name="writeStdIn">Function to write to stdin</param>
    /// <returns>An awaitable task</returns>
    public static async Task Execute(IEnumerable<string> command, string? workingDirectory = null, CancellationToken cancellationToken = default, Func<int, bool>? codeIsError = null, bool suppressStdErr = false, Func<StreamWriter, Task>? writeStdIn = null)
    {
        if (!command.Any())
            throw new ArgumentException("Needs at least one command", nameof(command));
        workingDirectory ??= Environment.CurrentDirectory;

        if (!Directory.Exists(workingDirectory))
            Directory.CreateDirectory(workingDirectory);

        codeIsError ??= (x) => x != 0;

        var p = Process.Start(new ProcessStartInfo(command.First(), command.Skip(1))
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = !suppressStdErr,
            RedirectStandardOutput = false,
            RedirectStandardInput = writeStdIn != null,
            UseShellExecute = false,
        }) ?? throw new Exception($"Failed to launch process {command.First()}, null returned");

        // Forward error messages to stderr
        var t = suppressStdErr
            ? Task.CompletedTask
            : p.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError(), cancellationToken);

        if (writeStdIn != null)
            await writeStdIn(p.StandardInput).ConfigureAwait(false);

        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (codeIsError(p.ExitCode))
            throw new Exception($"Execution of {command.First()} gave error code {p.ExitCode}");

        await t.ConfigureAwait(false);
    }

    /// <summary>
    /// Runs all commandline tasks in sequence
    /// </summary>
    /// <param name="commands">The commands to run</param>
    /// <param name="workingDirectory">The working directory to run in; <c>null</c> means current directory</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <param name="codeIsError">Callback method that is invoked with the error code from the process; the result indicates if the status code should be interpreted as an error.
    /// Default value is <c>null</c> which will treat anything non-zero as an error</param>
    /// <param name="suppressStdErr">If <c>true</c>, stderr is not forwarded to the console</param>
    /// <returns>An awaitable task</returns>
    public static async Task ExecuteAll(IEnumerable<IEnumerable<string>> commands, string? workingDirectory = null, CancellationToken cancellationToken = default, Func<int, bool>? codeIsError = null, bool suppressStdErr = false)
    {
        foreach (var c in commands)
            await Execute(c, workingDirectory, cancellationToken, codeIsError, suppressStdErr).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a commandline program and returns the contents of stdout
    /// </summary>
    /// <param name="command"></param>
    /// <param name="workingDirectory">The working directory to run in; <c>null</c> means current directory</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <param name="codeIsError">Callback method that is invoked with the error code from the process; the result indicates if the status code should be interpreted as an error.
    /// Default value is <c>null</c> which will treat anything non-zero as an error</param>
    /// <param name="suppressStdErr">If <c>true</c>, stderr is not forwarded to the console</param>
    /// <param name="writeStdIn">Function to write to stdin</param>
    /// <returns>The output from stdout</returns>
    public static async Task<string> ExecuteWithOutput(IEnumerable<string> command, string? workingDirectory = null, CancellationToken cancellationToken = default, Func<int, bool>? codeIsError = null, bool suppressStdErr = false, Func<StreamWriter, Task>? writeStdIn = null)
    {
        if (!command.Any())
            throw new ArgumentException("Needs at least one command", nameof(command));
        workingDirectory ??= Environment.CurrentDirectory;

        if (!Directory.Exists(workingDirectory))
            Directory.CreateDirectory(workingDirectory);

        codeIsError ??= (x) => x != 0;

        var p = Process.Start(new ProcessStartInfo(command.First(), command.Skip(1))
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = !suppressStdErr,
            RedirectStandardOutput = true,
            RedirectStandardInput = writeStdIn != null,
            UseShellExecute = false,
        }) ?? throw new Exception($"Failed to launch process {command.First()}, null returned");

        var tstdout = p.StandardOutput.ReadToEndAsync(cancellationToken);
        var tstderr = suppressStdErr
            ? Task.CompletedTask
            : p.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError(), cancellationToken);

        if (writeStdIn != null)
            await writeStdIn(p.StandardInput).ConfigureAwait(false);

        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (codeIsError(p.ExitCode))
            throw new Exception($"Execution of {command.First()} gave error code {p.ExitCode}");

        await tstderr.ConfigureAwait(false);
        return await tstdout.ConfigureAwait(false);
    }


    /// <summary>
    /// Starts a commandline program and returns the contents of stdout
    /// </summary>
    /// <param name="command"></param>
    /// <param name="stdout">The stream to write the output to</param>
    /// <param name="workingDirectory">The working directory to run in; <c>null</c> means current directory</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <param name="codeIsError">Callback method that is invoked with the error code from the process; the result indicates if the status code should be interpreted as an error.
    /// Default value is <c>null</c> which will treat anything non-zero as an error</param>
    /// <param name="suppressStdErr">If <c>true</c>, stderr is not forwarded to the console</param>
    /// <param name="writeStdIn">Function to write to stdin</param>
    /// <returns>The output from stdout</returns>
    public static async Task ExecuteWithOutput(IEnumerable<string> command, Stream stdout, string? workingDirectory = null, CancellationToken cancellationToken = default, Func<int, bool>? codeIsError = null, bool suppressStdErr = false, Func<StreamWriter, Task>? writeStdIn = null)
    {
        if (!command.Any())
            throw new ArgumentException("Needs at least one command", nameof(command));
        workingDirectory ??= Environment.CurrentDirectory;

        if (!Directory.Exists(workingDirectory))
            Directory.CreateDirectory(workingDirectory);

        codeIsError ??= (x) => x != 0;

        var p = Process.Start(new ProcessStartInfo(command.First(), command.Skip(1))
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = !suppressStdErr,
            RedirectStandardOutput = true,
            RedirectStandardInput = writeStdIn != null,
            UseShellExecute = false,
        }) ?? throw new Exception($"Failed to launch process {command.First()}, null returned");

        var tstdout = p.StandardOutput.BaseStream.CopyToAsync(stdout, cancellationToken);
        var tstderr = suppressStdErr
            ? Task.CompletedTask
            : p.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError(), cancellationToken);

        if (writeStdIn != null)
            await writeStdIn(p.StandardInput).ConfigureAwait(false);

        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (codeIsError(p.ExitCode))
            throw new Exception($"Execution of {command.First()} gave error code {p.ExitCode}");

        await tstderr.ConfigureAwait(false);
        await tstdout.ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a commandline program and waits for it to complete
    /// </summary>
    /// <param name="command"></param>
    /// <param name="workingDirectory">The working directory to run in; <c>null</c> means current directory</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <param name="codeIsError">Callback method that is invoked with the error code from the process; the result indicates if the status code should be interpreted as an error.
    /// <param name="logFolder"/>The folder where the log files are written</param>
    /// <param name="logFilename">Function to create custom filenames for the log files</param>
    /// Default value is <c>null</c> which will treat anything non-zero as an error</param>
    /// <param name="writeStdIn">Function to write to stdin</param>
    /// <returns>The output from stdout</returns>
    public static async Task ExecuteWithLog(IEnumerable<string> command, string? workingDirectory = null, CancellationToken cancellationToken = default, Func<int, bool>? codeIsError = null, string? logFolder = null, Func<int, bool, string>? logFilename = null, Func<StreamWriter, Task>? writeStdIn = null)
    {
        if (!command.Any())
            throw new ArgumentException("Needs at least one command", nameof(command));
        workingDirectory ??= Environment.CurrentDirectory;

        if (!Directory.Exists(workingDirectory))
            Directory.CreateDirectory(workingDirectory);

        logFolder ??= workingDirectory;

        codeIsError ??= (x) => x != 0;

        var p = Process.Start(new ProcessStartInfo(command.First(), command.Skip(1))
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = writeStdIn != null,
            UseShellExecute = false,
        }) ?? throw new Exception($"Failed to launch process {command.First()}, null returned");

        logFilename ??= (pid, isStdOut) => $"{command.First()}-{p.Id}.{(isStdOut ? "stdout" : "stderr")}.log";

        using var logstdout = File.Create(Path.Combine(logFolder, logFilename(p.Id, true)));
        using var logstderr = File.Create(Path.Combine(logFolder, logFilename(p.Id, false)));

        var t1 = p.StandardOutput.BaseStream.CopyToAsync(logstdout, cancellationToken);
        var t2 = p.StandardError.BaseStream.CopyToAsync(logstderr, cancellationToken);

        if (writeStdIn != null)
            await writeStdIn(p.StandardInput).ConfigureAwait(false);

        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (codeIsError(p.ExitCode))
            throw new Exception($"Execution of {command.First()} gave error code {p.ExitCode}");

        await t1.ConfigureAwait(false);
        await t2.ConfigureAwait(false);
    }
}
