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
using System.IO;

namespace Duplicati.Library.Compression
{
    /// <summary>
    /// CompressionStream wrapper to prevent closing the base stream when disposing the entry stream
    /// </summary>
    public class StreamWrapper : Utility.OverrideableStream
    {
        /// <summary>
        /// The callback to invoke when the stream is disposed
        /// </summary>
        private readonly Action<StreamWrapper> m_onCloseCallback;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Compression.StreamWrapper"/> class.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="onClose">On close.</param>
        public StreamWrapper(Stream stream, Action<StreamWrapper> onClose)
            : base(stream)
        {
            m_onCloseCallback = onClose;
        }

        /// <summary>
        /// Disposes the stream, and calls the close handler if required
        /// </summary>
        /// <param name="disposing">If set to <c>true</c>, this call originates from the dispose method.</param>
        protected override void Dispose(bool disposing)
        {
            if (!disposing || m_basestream == null) return;

            m_basestream.Dispose();
            m_basestream = null;

            if (m_onCloseCallback != null)
                m_onCloseCallback(this);
        }

        public long GetSize()
        {
            return m_basestream == null ? 0 : m_basestream.Length;
        }
    }
}
