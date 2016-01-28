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
    public class EventProcessor : ShutdownHelper
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
        private readonly TimeSpan WAIT_TIME = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The input channel for receiving events
        /// </summary>
        internal readonly IWriteChannel<ReportItem> Channel;

        /// <summary>
        /// The input channel for receiving events
        /// </summary>
        private readonly IChannel<ReportItem> m_channel;

        /// <summary>
        /// The completion task
        /// </summary>
        public readonly Task Terminated;

        /// <summary>
        /// The forwarding destination
        /// </summary>
        private readonly IWriteChannel<string> m_forward;

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
        /// The current process ID
        /// </summary>
        private readonly string INSTANCE_ID;

        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.UsageReporter.EventProcessor"/> class.
        /// </summary>
        /// <param name="forwardChannel">The forwarding destination.</param>
        public EventProcessor(IWriteChannel<string> forwardChannel)
        {
            INSTANCE_ID = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();

            Channel = m_channel = ChannelManager.CreateChannel<ReportItem>(null, MAX_QUEUE_SIZE);
            m_forward = forwardChannel;
            Terminated = RunProtected(Run);
        }

        /// <summary>
        /// Run the processing of incomming requests
        /// </summary>
        private async Task Run()
        {
            var rs = new ReportSet();
            var tf = GetTempFilename();

            foreach(var f in GetAbandonedFiles(null))
                await m_forward.WriteAsync(f);

            while(true)
            {
                var forceSend = false;
                try
                {
                    var item = await m_channel.ReadAsync(rs.Items.Count == 0 ? Timeout.Infinite : WAIT_TIME);
                    if (item != null)
                    {
                        forceSend = item.Type == ReportType.Crash;
                        rs.Items.Add(item);
                        File.WriteAllText(tf, JsonConvert.SerializeObject(rs));
                    }
                }
                catch(TimeoutException)
                {
                    forceSend = true;
                }

                if ((forceSend && rs.Items.Count > 0) || (rs.Items.Count > MAX_ITEMS_IN_SET))
                {
                    var nextFilename = GetTempFilename();
                    await m_forward.WriteAsync(tf);
                    rs = new ReportSet();

                    foreach(var f in GetAbandonedFiles(tf))
                        await m_forward.WriteAsync(f);

                    tf = nextFilename;
                }
            }
        }

        private IEnumerable<string> GetAbandonedFiles(string current)
        {
            return 
                from n in GetAbandonedMatches(current)
                orderby n.Value
                select n.Key;
        }

        private IEnumerable<KeyValuePair<string, long>> GetAbandonedMatches(string current)
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

        private string GetTempFilename()
        {
            return Path.Combine(Path.GetTempPath(), string.Format(FILENAME_TEMPLATE, INSTANCE_ID, DateTime.UtcNow.ToString("yyyyMMddHHmmss")));
        }
    }
}

