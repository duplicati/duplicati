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
using CoCoL;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;

namespace Duplicati.Library.UsageReporter
{
    public class ReportSetUploader : ShutdownHelper
    {
        /// <summary>
        /// The maximum number of pending uploads
        /// </summary>
        private const int MAX_PENDING_UPLOADS = 500;

        /// <summary>
        /// The target upload url
        /// </summary>
        private const string UPLOAD_URL = "https://usage-reporter.duplicati.com/api/v1/report";

        /// <summary>
        /// The input channel for receiving events
        /// </summary>
        private readonly IChannel<string> m_channel;

        /// <summary>
        /// The input channel for receiving events
        /// </summary>
        public readonly IWriteChannel<string> Channel;

        /// <summary>
        /// The internal channel for passing filtered requests
        /// </summary>
        private readonly IChannel<string> FilterChannel;

        /// <summary>
        /// The completion task
        /// </summary>
        public readonly Task Terminated;

        public ReportSetUploader()
        {
            Channel = m_channel = ChannelManager.CreateChannel<string>(buffersize: MAX_PENDING_UPLOADS);
            FilterChannel = ChannelManager.CreateChannel<string>();

            Terminated = Task.WhenAll(
                RunProtected(async () => {
                    // If we have more than MAX_PENDING_UPLOADS
                    // the newest ones are just discarded
                    // they will be picked up on the later runs

                    while (true)
                        FilterChannel.TryWrite(await m_channel.ReadAsync());
                }),
                RunProtected(Run)
            );
        }

        /// <summary>
        /// Run the processing of incomming requests
        /// </summary>
        private async Task Run()
        {
            while (true)
            {
                var f = await FilterChannel.ReadAsync();

                try
                {
                    if (File.Exists(f))
                    {
                        var req = (HttpWebRequest)WebRequest.Create(UPLOAD_URL);
                        req.Method = "POST";
                        req.ContentType = "application/json; charset=utf-8";

                        int rc;
                        using(var fs = File.OpenRead(f))
                        {
                            req.ContentLength = fs.Length;
                            var areq = new Library.Utility.AsyncHttpRequest(req);

                            using(var rs =areq.GetRequestStream())
                                Library.Utility.Utility.CopyStream(fs, rs);

                            using(var resp = (HttpWebResponse)areq.GetResponse())
                                rc = (int)resp.StatusCode;
                        }

                        if (rc >= 200 && rc <= 299)
                            File.Delete(f);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteMessage("UsageReporter failed", Duplicati.Library.Logging.LogMessageType.Error, ex);
                }
            }        
        }
    }
}

