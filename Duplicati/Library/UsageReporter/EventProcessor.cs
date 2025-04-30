// Copyright (C) 2025, The Duplicati Team
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
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using CoCoL;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Duplicati.Library.UsageReporter
{
    public static class EventProcessor
    {
        /// <summary>
        /// The maximum number of events to collect before transmitting
        /// </summary>
        private const int MAX_ITEMS_IN_SET = 20;

        /// <summary>
        /// Max number of events to queue up before giving up
        /// </summary>
        private const int MAX_QUEUE_SIZE = 500;

        /// <summary>
        /// The maximum number of backlog files to consider
        /// </summary>
        private const int MAX_BACKLOG = 6;

        /// <summary>
        /// The maximum time a backlog file is considered valid
        /// </summary>
        private static readonly TimeSpan MAX_BACKLOG_AGE = TimeSpan.FromDays(30);

        /// <summary>
        /// The time to wait before sending event
        /// </summary>
        private static readonly TimeSpan WAIT_TIME = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The prefix for generated filenames
        /// </summary>
        private const string FILENAME_PREFIX = "dupl-usagereport";

        /// <summary>
        /// The template used to create filenames
        /// </summary>
        private const string FILENAME_TEMPLATE = FILENAME_PREFIX + "-{0}-{1}.json";

        /// <summary>
        /// The regular expression that matches a filename
        /// </summary>
        private static readonly Regex FILNAME_MATCHER = new Regex(string.Format(FILENAME_TEMPLATE, "(?<id>[0-9]+)", "(?<time>[0-9]+)"));

        /// <summary>
        /// Runs the report processor
        /// </summary>
        /// <param name="forward">The channel accepting filenames with usage reports.</param>
        internal static Tuple<Task, IWriteChannel<ReportItem>> Run(IWriteChannel<string> forward)
        {
            var instanceid = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
            var channel = ChannelManager.CreateChannel<ReportItem>(
                maxPendingWriters: MAX_QUEUE_SIZE,
                pendingWritersOverflowStrategy: QueueOverflowStrategy.LIFO
            );

            var task = AutomationExtensions.RunTask(
                new
                {
                    Input = channel.AsRead(),
                    Output = forward
                },
                async (self) =>
                {
                    // Wait 20 seconds before we start transmitting
                    for(var i = 0; i < 20; i++)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                        if (await self.Input.IsRetiredAsync)
                            return;
                    }

                    await ProcessAbandonedFiles(self.Output, self.Input, null).ConfigureAwait(false);

                    var rs = new ReportSet();
                    var tf = GetTempFilename(instanceid);
                    var nextTransmitTarget = new DateTime(0);

                    while (true)
                    {
                        var forceSend = false;
                        try
                        {
                            // We wait until we get an item, or WAIT_TIME from the last event
                            var waittime =
                                    rs.Items.Count == 0
                                      ? Timeout.Infinite
                                      : new TimeSpan(Math.Max(0, (nextTransmitTarget - DateTime.UtcNow).Ticks));
                        
                            var item = await self.Input.ReadAsync(waittime);
                            if (item != null)
                            {
                                if (rs.Items.Count == 0)
                                    nextTransmitTarget = DateTime.UtcNow + WAIT_TIME;
                            
                                forceSend = item.Type == ReportType.Crash;
                                rs.Items.Add(item);
                                File.WriteAllText(tf, JsonConvert.SerializeObject(rs));
                            }
                        }
                        catch (TimeoutException)
                        {
                            forceSend = true;
                        }

                        if ((forceSend && rs.Items.Count > 0) || (rs.Items.Count > MAX_ITEMS_IN_SET))
                        {
                            var nextFilename = GetTempFilename(instanceid);
                            self.Output.WriteNoWait(tf);
                            rs = new ReportSet();

                            await ProcessAbandonedFiles(self.Output, self.Input, null).ConfigureAwait(false);

                            tf = nextFilename;
                        }
                    }
                }
            );

            return new Tuple<Task, IWriteChannel<ReportItem>>(task, channel);
        }


        /// <summary>
        /// Transmits abandoned files to the server, and enforces discard rules
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="target">The target channel.</param>
        /// <param name="source">The source channel, used to check for stopping conditions</param>
        /// <param name="current">The current file, which should not be uploaded.</param>
        private static async Task ProcessAbandonedFiles(IWriteChannel<string> target, IReadChannel<ReportItem> source, string current)
        {
            var abandonLeft = MAX_BACKLOG;
            var abandonCutOff = long.Parse(DateTime.UtcNow.Add(-MAX_BACKLOG_AGE).ToString("yyyyMMddHHmmss"));
            foreach (var f in GetAbandonedFiles(current))
            {
                if (await source.IsRetiredAsync)
                    return;

                if (abandonLeft > 0 && f.Value > abandonCutOff)
                {
                    abandonLeft--;
                    target.WriteNoWait(f.Key);
                }
                else
                {
                    try { File.Delete(f.Key); }
                    catch { }
                }
            }

        }

        /// <summary>
        /// Gets a list of abandoned files, meaning files that appear to be Duplicati files.
        /// These should have been uploaded, but if they are found they are somehow left-over.
        /// </summary>
        /// <returns>The abandoned files, value is the timestamp, key is the filename.</returns>
        /// <param name="current">The current file, which is excluded from the results.</param>
        private static IEnumerable<KeyValuePair<string, long>> GetAbandonedFiles(string current)
        {
            return
                GetAbandonedMatches(current)
                            .OrderByDescending(x => x.Value);
        }

        /// <summary>
        /// Gets files that match the temporary file prefix
        /// </summary>
        /// <returns>The abandoned matches.</returns>
        /// <param name="current">The current file, which is excluded from the results.</param>
        private static IEnumerable<KeyValuePair<string, long>> GetAbandonedMatches(string current)
        {
            foreach(var f in Directory.EnumerateFiles(Duplicati.Library.Utility.TempFolder.SystemTempPath, FILENAME_PREFIX + "*", SearchOption.TopDirectoryOnly))
            {
                var selfname = Path.GetFileName(f);
                var m = FILNAME_MATCHER.Match(selfname);
                if (m.Success)
                {
                    if (selfname == current)
                        continue;
                    
                    try
                    {
                        if (System.Diagnostics.Process.GetProcessById(int.Parse(m.Groups["id"].Value)) != null)
                            continue;
                    }
                    catch
                    {
                    }

                    yield return new KeyValuePair<string, long>(f, long.Parse(m.Groups["time"].Value));
                }
            }
        }

        /// <summary>
        /// Gets a unique timestamped filename using the template
        /// </summary>
        /// <returns>The temporary filename.</returns>
        /// <param name="instanceid">The instance ID of this process.</param>
        private static string GetTempFilename(string instanceid)
        {
            return Path.Combine(Duplicati.Library.Utility.TempFolder.SystemTempPath, string.Format(FILENAME_TEMPLATE, instanceid, DateTime.UtcNow.ToString("yyyyMMddHHmmss")));
        }
    }
}

