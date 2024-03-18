// Copyright (C) 2024, The Duplicati Team
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
using CoCoL;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;

namespace Duplicati.Library.UsageReporter
{
    public static class ReportSetUploader
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ReportSetUploader));

        /// <summary>
        /// The maximum number of pending uploads
        /// </summary>
        private const int MAX_PENDING_UPLOADS = 50;

        /// <summary>
        /// The target upload url
        /// </summary>
        private const string UPLOAD_URL = "https://usage-reporter.duplicati.com/api/v1/report";

        /// <summary>
        /// Runs the upload process
        /// </summary>
        /// <returns>A tuple with the completion task and the channel to use</returns>
        public static Tuple<Task, IWriteChannel<string>> Run()
        {
            var channel = ChannelManager.CreateChannel<string>(
                buffersize: MAX_PENDING_UPLOADS, 
                pendingWritersOverflowStrategy: QueueOverflowStrategy.LIFO
            );

            var task = AutomationExtensions.RunTask(
                channel.AsRead(),

                async (chan) =>
                {
                    while (true)
                    {
                        var f = await chan.ReadAsync();

                        try
                        {
                            if (File.Exists(f))
                            {
                                var req = (HttpWebRequest)WebRequest.Create(UPLOAD_URL);
                                req.Method = "POST";
                                req.ContentType = "application/json; charset=utf-8";

                                int rc;
                                using (var fs = File.OpenRead(f))
                                {
                                    if (fs.Length > 0)
                                    {
                                        req.ContentLength = fs.Length;
                                        var areq = new Library.Utility.AsyncHttpRequest(req);

                                        using (var rs = areq.GetRequestStream())
                                            Library.Utility.Utility.CopyStream(fs, rs);

                                        using (var resp = (HttpWebResponse)areq.GetResponse())
                                            rc = (int)resp.StatusCode;
                                    }
                                    else
                                        rc = 200;
                                }

                                if (rc >= 200 && rc <= 299)
                                    File.Delete(f);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteErrorMessage(LOGTAG, "UploadFailed", ex, "UsageReporter failed");
                        }
                    }
                }
            );

            return new Tuple<Task, IWriteChannel<string>>(task, channel);
        }

    }
}

