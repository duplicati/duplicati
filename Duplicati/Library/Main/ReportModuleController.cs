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
using System.Threading;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Interface;
using Microsoft.VisualStudio.Threading;

namespace Duplicati.Library.Main;

/// <summary>
/// Static helper that wires up <see cref="IReportModule"/> instances for an
/// operation, owns the progress ticker, and provides the disposable handle that
/// fires the completion callbacks. Extracted from <see cref="Controller"/> to keep
/// the report-module plumbing in one place.
/// </summary>
internal static class ReportModuleController
{
    /// <summary>
    /// The tag used for logging.
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<ReportModuleAdapter>();

    /// <summary>
    /// The interval at which progress snapshots are pushed to report modules.
    /// Report modules decide for themselves how often to act on these ticks.
    /// </summary>
    public static readonly TimeSpan ProgressTickInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Wires up any loaded <see cref="IReportModule"/> instances on the given
    /// controller so they observe the operation's lifecycle, backend events, log
    /// entries and progress. Each active module is wrapped in a
    /// <see cref="ReportModuleAdapter"/> appended to the controller's message sink,
    /// and a periodic ticker is started that pushes progress snapshots to the modules.
    /// </summary>
    /// <param name="controller">The controller running the operation.</param>
    /// <param name="result">The result object for the operation.</param>
    /// <param name="options">The options containing the loaded report modules.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A handle that completes the report modules when disposed.</returns>
    public static async Task<ReportModulesHandle?> SetupAsync(Controller controller, IBasicResults result, Options options, CancellationToken cancellationToken)
    {
        var adapters = new List<ReportModuleAdapter>();
        var loadedModules = options.LoadedModules;
        if (loadedModules != null)
        {
            foreach (var mx in loadedModules)
            {
                if (mx is not IReportModule reportModule || !reportModule.IsActive)
                    continue;

                var adapter = new ReportModuleAdapter(reportModule, cancellationToken);
                adapters.Add(adapter);
                controller.AppendSinkAsync(adapter).FireAndForget();

                try
                {
                    await reportModule.OnOperationStartedAsync(options.MainAction.ToString(), result, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, $"ReportModuleStartError{reportModule.Key}", ex, "Report module {0} OnOperationStartedAsync failed: {1}", reportModule.Key, ex.Message);
                }
            }
        }

        // When no report module is active there is nothing to wire up: avoid the
        // progress ticker and leave the message sink untouched so the hot path
        // keeps its original (zero-overhead) behavior.
        if (adapters.Count == 0)
            return default;

        var tickerCts = new CancellationTokenSource();
        var ticker = StartProgressTicker(adapters, tickerCts.Token);

        return new ReportModulesHandle(adapters, result, ticker, tickerCts);
    }

    /// <summary>
    /// Starts a background loop that periodically pushes progress snapshots to the
    /// given report module adapters. The loop stops when the cancellation token is
    /// cancelled, and the returned task is awaited by the handle on dispose.
    /// </summary>
    /// <param name="adapters">The adapters to tick.</param>
    /// <param name="cancellationToken">The token that stops the loop.</param>
    /// <returns>A task that completes when the loop stops.</returns>
    private static Task StartProgressTicker(IReadOnlyList<ReportModuleAdapter> adapters, CancellationToken cancellationToken)
    {
        if (adapters.Count == 0)
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(ProgressTickInterval);
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    foreach (var adapter in adapters)
                        await adapter.TickAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected when the operation ends */ }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ReportModuleTickerError", ex, "Report module progress ticker failed: {0}", ex.Message);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Handle that completes report modules when disposed: it stops the progress
    /// ticker and fires <see cref="IReportModule.OnOperationCompletedAsync"/> on each
    /// module. It is disposed in <see cref="Controller.OperationCompleteAsync"/> before
    /// the module instances themselves are disposed.
    /// </summary>
    public class ReportModulesHandle : IDisposable
    {
        private readonly List<ReportModuleAdapter> m_adapters;
        private readonly IBasicResults m_result;
        private readonly Task m_ticker;
        private readonly CancellationTokenSource m_tickerCts;

        public ReportModulesHandle(List<ReportModuleAdapter> adapters, IBasicResults result, Task ticker, CancellationTokenSource tickerCts)
        {
            m_adapters = adapters;
            m_result = result;
            m_ticker = ticker;
            m_tickerCts = tickerCts;
        }

        public Exception? OperationException { get; set; }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // Stop the ticker first so no more progress ticks race the completion callbacks.
            try { m_tickerCts?.Cancel(); } catch { }

            if (m_ticker != null)
                try { await m_ticker.WithCancellation(cancellationToken); } catch { }

            if (m_adapters != null)
            {
                // Signal all modules to stop processing
                var tasks = new List<Task>();
                foreach (var adapter in m_adapters)
                {
                    try
                    {
                        tasks.Add(adapter.Module.OnOperationCompletedAsync(m_result, OperationException, cancellationToken));
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, $"ReportModuleCompleteError{adapter.Module.Key}", ex, "Report module {0} OnOperationCompletedAsync failed: {1}", adapter.Module.Key, ex.Message);
                    }
                }

                if (tasks.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(tasks)
                            .WithCancellation(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "ReportModuleCompletionError", ex, "Report module completion callbacks failed: {0}", ex.Message);
                    }
                }
            }

            m_tickerCts?.Dispose();
        }

        public void Dispose()
        {
            try { m_tickerCts?.Cancel(); } catch { }
            m_tickerCts?.Dispose();

        }
    }
}

