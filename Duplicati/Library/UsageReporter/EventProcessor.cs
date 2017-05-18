//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        if (self.Input.IsRetired)
                            return;
                    }

                    foreach (var f in GetAbandonedFiles(null))
                    {
                        // Check if we should exit
                        if (self.Input.IsRetired)
                            return;
                    
                        await self.Output.WriteAsync(f);
                    }

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

                            foreach (var f in GetAbandonedFiles(tf))
                            {
                                if (self.Input.IsRetired)
                                    return;
                            
                                self.Output.WriteNoWait(f);
                            }

                            tf = nextFilename;
                        }
                    }
                }
            );

            return new Tuple<Task, IWriteChannel<ReportItem>>(task, channel);
        }

        /// <summary>
        /// Gets a list of abandoned files, meaning files that appear to be Duplicati files.
        /// These should have been uploaded, but if they are found they are somehow left-over.
        /// </summary>
        /// <returns>The abandoned files.</returns>
        /// <param name="current">The current file, which is excluded from the results.</param>
        private static IEnumerable<string> GetAbandonedFiles(string current)
        {
            return 
                from n in GetAbandonedMatches(current)
                orderby n.Value
                select n.Key;
        }

        /// <summary>
        /// Gets files that match the temporary file prefix
        /// </summary>
        /// <returns>The abandoned matches.</returns>
        /// <param name="current">The current file, which is excluded from the results.</param>
        private static IEnumerable<KeyValuePair<string, long>> GetAbandonedMatches(string current)
        {
            foreach(var f in Directory.EnumerateFiles(Path.GetTempPath(), FILENAME_PREFIX + "*", SearchOption.TopDirectoryOnly))
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
            return Path.Combine(Path.GetTempPath(), string.Format(FILENAME_TEMPLATE, instanceid, DateTime.UtcNow.ToString("yyyyMMddHHmmss")));
        }
    }
}

