using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Threading;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This class wraps a HttpWebRequest and performs GetRequestStream and GetResponseStream
    /// with async methods while maintaining a synchronous interface
    /// </summary>
    public class AsyncHttpRequest
    {
        /// <summary>
        /// The <see cref="System.Net.HttpWebRequest"/> method being wrapped
        /// </summary>
        private WebRequest m_request;
        /// <summary>
        /// The current internal state of the object
        /// </summary>
        private RequestStates m_state = RequestStates.Created;
        /// <summary>
        /// The request async wrapper
        /// </summary>
        private AsyncWrapper m_asyncRequest = null;
        /// <summary>
        /// The response async wrapper
        /// </summary>
        private AsyncWrapper m_asyncResponse = null;
        /// <summary>
        /// The request/response timeout value
        /// </summary>
        private int m_timeout = 100000;
        /// <summary>
        /// The activity timeout value
        /// </summary>
        private int m_activity_timeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;

        /// <summary>
        /// List of valid states
        /// </summary>
        private enum RequestStates
        {
            /// <summary>
            /// The request has been created
            /// </summary>
            Created,
            /// <summary>
            /// The request stream has been requested
            /// </summary>
            GetRequest,
            /// <summary>
            /// The response has been requested
            /// </summary>
            GetResponse,
            /// <summary>
            /// 
            /// </summary>
            Done
        }

        /// <summary>
        /// Constructs a new request from a url
        /// </summary>
        /// <param name="url">The url to create the request from</param>
        public AsyncHttpRequest(string url)
            : this(System.Net.WebRequest.Create(url))
        {

        }

        /// <summary>
        /// Creates a async request wrapper for an existing url
        /// </summary>
        /// <param name="request">The request to wrap</param>
        public AsyncHttpRequest(WebRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");
            m_request = request;
            m_timeout = m_request.Timeout;

            //We set this to prevent timeout related stuff from happening outside this module
            m_request.Timeout = System.Threading.Timeout.Infinite;

            //Then we register a custom setting of 30 secs timeout on read/write activity
            if (m_request is HttpWebRequest)
            {
                if (((HttpWebRequest)m_request).ReadWriteTimeout != System.Threading.Timeout.Infinite)
                    m_activity_timeout = ((HttpWebRequest)m_request).ReadWriteTimeout;

                ((HttpWebRequest)m_request).ReadWriteTimeout = System.Threading.Timeout.Infinite;

                // Prevent in-memory buffering causing out-of-memory issues
                ((HttpWebRequest)m_request).AllowReadStreamBuffering = false;                    
            }
        }

        /// <summary>
        /// Gets the request that is wrapped
        /// </summary>
        public WebRequest Request { get { return m_request; } }

        /// <summary>
        /// Gets or sets the timeout used to guard the <see cref="GetRequestStream(long)"/> and <see cref="GetResponse()"/> calls
        /// </summary>
        public int Timeout { get { return m_timeout; } set { m_timeout = value; } }

        /// <summary>
        /// Gets the request stream
        /// </summary>
        /// <returns>The request stream</returns>
        /// <param name="contentlength">The content length to use</param>
        public Stream GetRequestStream(long contentlength = -1)
        {
            // Prevent in-memory buffering causing out-of-memory issues
            if (m_request is HttpWebRequest)
            {
                if (contentlength >= 0)
                    ((HttpWebRequest)m_request).ContentLength = contentlength;
                if (m_request.ContentLength >= 0)
                    ((HttpWebRequest)m_request).AllowWriteStreamBuffering = false;
            }

            if (m_state == RequestStates.GetRequest)
                return (Stream)m_asyncRequest.GetResponseOrStream();

            if (m_state != RequestStates.Created)
                throw new InvalidOperationException();

            m_asyncRequest = new AsyncWrapper(this, true);
            m_state = RequestStates.GetRequest;

            return TrySetTimeout((Stream)m_asyncRequest.GetResponseOrStream(), m_activity_timeout);
        }

        /// <summary>
        /// Gets the response object
        /// </summary>
        /// <returns>The web response</returns>
        public WebResponse GetResponse()
        {
            if (m_state == RequestStates.GetResponse)
                return (WebResponse)m_asyncResponse.GetResponseOrStream();

            if (m_state == RequestStates.Done)
                throw new InvalidOperationException();

            m_asyncRequest = null;
            m_asyncResponse = new AsyncWrapper(this, false);
            m_state = RequestStates.GetResponse;

            return (WebResponse)m_asyncResponse.GetResponseOrStream();
        }

        public Stream GetResponseStream()
        {
            return TrySetTimeout(GetResponse().GetResponseStream(), m_activity_timeout);
        }

        public static Stream TrySetTimeout(Stream str, int timeoutmilliseconds = 30000)
        {
            try { str.ReadTimeout = timeoutmilliseconds; }
            catch { }

            return str;
        }
            
        /// <summary>
        /// Wrapper class for getting request and respone objects in a async manner
        /// </summary>
        private class AsyncWrapper
        {
            private IAsyncResult m_async = null;
            private Stream m_stream = null;
            private WebResponse m_response = null;
            private AsyncHttpRequest m_owner;
            private Exception m_exception = null;
            private ManualResetEvent m_event = new ManualResetEvent(false);
            private bool m_isRequest;
            private bool m_timedout = false;

            public AsyncWrapper(AsyncHttpRequest owner, bool isRequest)
            {
                m_owner = owner;
                m_isRequest = isRequest;

                if (m_isRequest)
                    m_async = m_owner.m_request.BeginGetRequestStream(new AsyncCallback(this.OnAsync), null);
                else
                    m_async = m_owner.m_request.BeginGetResponse(new AsyncCallback(this.OnAsync), null);

                if ( m_owner.m_timeout != System.Threading.Timeout.Infinite)
                    ThreadPool.RegisterWaitForSingleObject(m_async.AsyncWaitHandle, new WaitOrTimerCallback(this.OnTimeout), null, TimeSpan.FromMilliseconds( m_owner.m_timeout), true);
            }

            private void OnAsync(IAsyncResult r)
            {
                try
                {
                    if (m_isRequest)
                        m_stream = m_owner.m_request.EndGetRequestStream(r);
                    else
                        m_response = m_owner.m_request.EndGetResponse(r);
                }
                catch (Exception ex)
                {
                    if (m_timedout)
                        m_exception = new WebException(string.Format("{0} timed out", m_isRequest ? "GetRequestStream" : "GetResponse"), ex, WebExceptionStatus.Timeout, ex is WebException ? ((WebException)ex).Response : null);
                    else
                    {
                        // Workaround for: https://bugzilla.xamarin.com/show_bug.cgi?id=28287
                        var wex = ex;
                        if (ex is WebException && ((WebException)ex).Response == null)
                        {
                            WebResponse resp = null;

                            try { resp = (WebResponse)r.GetType().GetProperty("Response", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(r); }
                            catch {}

                            if (resp == null)
                                try { resp = (WebResponse)m_owner.m_request.GetType().GetField("webResponse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(m_owner.m_request); }
                                catch { }

                            if (resp != null)
                                wex = new WebException(ex.Message, ex.InnerException, ((WebException)ex).Status, resp);
                        }

                        m_exception = wex;


                    }
                }
                finally
                {
                    m_event.Set();
                }
            }

            private void OnTimeout(object state, bool timedout)
            {
                if (timedout)
                {
                    if (!m_event.WaitOne(0, false))
                    {
                        m_timedout = true;
                        m_owner.m_request.Abort();
                    }
                }
            }

            public object GetResponseOrStream()
            {
                try
                {
                    m_event.WaitOne();
                }
                catch (ThreadAbortException)
                {
                    m_owner.m_request.Abort();
                    
                    //Grant a little time for cleanups
                    m_event.WaitOne((int)TimeSpan.FromSeconds(5).TotalMilliseconds, false);

                    //The abort exception will automatically be rethrown
                }

                if (m_exception != null)
                    throw m_exception;

                if (m_isRequest)
                    return m_stream;
                else
                    return m_response;
            }
        }
    }
}
