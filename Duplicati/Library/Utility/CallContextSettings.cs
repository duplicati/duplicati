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
using System.Collections.Concurrent;
using System.Threading;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Utility
{
    public static class SystemContextSettings
    {
        private static string defaultTempPath = null;

        private struct SystemSettings
        {
            public string Tempdir;
            public long Buffersize;
        }

        public static string DefaultTempPath
        {
            get
            {
                return defaultTempPath ?? System.IO.Path.GetTempPath();
            }
            set
            {
                if (!System.IO.Directory.Exists(value))
                    throw new FolderMissingException(Strings.TempFolder.TempFolderDoesNotExistError(value));
                defaultTempPath = value;
            }
        }

        public static IDisposable StartSession(string tempdir = null, long buffersize = 0)
        {
            if (buffersize < 1024)
                buffersize = 64 * 1024;

            var systemSettings = new SystemSettings
            {
                Tempdir = string.IsNullOrWhiteSpace(tempdir) ? DefaultTempPath : tempdir,
                Buffersize = buffersize
            };
            return CallContextSettings<SystemSettings>.StartContext(systemSettings);
        }

        public static string Tempdir
        {
            get
            {
                var tf = CallContextSettings<SystemSettings>.Settings.Tempdir;

                if (string.IsNullOrWhiteSpace(tf))
                {
                    tf = DefaultTempPath;
                }
                return tf;
            }
            set
            {
                var st = CallContextSettings<SystemSettings>.Settings;
                st.Tempdir = value;
                CallContextSettings<SystemSettings>.Settings = st;
            }
        }

        public static long Buffersize
        {
            get
            {
                var bs = CallContextSettings<SystemSettings>.Settings.Buffersize;
                if (bs < 1024)
                    bs = 64 * 1024;
                return bs;
            }
        }
    }

    /// <summary>
    /// Help class for providing settings in the current call context
    /// </summary>
    public static class CallContextSettings<T>
    {
        /// <summary>
        /// The call context settings for OAuth
        /// </summary>
        private static readonly AsyncLocal<T> _settings = new AsyncLocal<T>();

        /// <summary>
        /// The instances that are currently active
        /// </summary>
        private static readonly ConcurrentDictionary<T, object> _instances = new ConcurrentDictionary<T, object>();

        /// <summary>
        /// Disposable class for setting call context settings
        /// </summary>
        /// <typeparam name="T">The type of the settings</typeparam>
        private sealed class Disposer : IDisposable
        {
            /// <summary>
            /// A flag indicating if the object has been disposed
            /// </summary>
            private bool m_disposed = false;
            /// <summary>
            /// The previous value
            /// </summary>
            private T m_prev;
            /// <summary>
            /// The current value
            /// </summary>
            private T m_current;

            /// <summary>
            /// Initializes a new instance of the <see cref="Disposer"/> class.
            /// </summary>
            /// <param name="current">The current server URL</param>
            /// <param name="prev">The previous server URL</param>
            public Disposer(T current, T prev)
            {
                _settings.Value = m_current = current;
                this.m_prev = prev;
                _instances.TryAdd(current, null);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (m_disposed)
                    return;
                GC.SuppressFinalize(this);
                m_disposed = true;
                if (_settings.Value.Equals(m_current))
                    _settings.Value = m_prev;
                _instances.TryRemove(m_current, out _);
            }
        }

        /// <summary>
        /// Gets or sets the values in the current call context
        /// </summary>
        /// <value>The settings.</value>
        public static T Settings
        {
            get => _settings.Value;
            set => _settings.Value = value;
        }

        /// <summary>
        /// Starts the context wit a default value.
        /// </summary>
        /// <returns>The disposable handle for the context.</returns>
        /// <param name="initial">The initial value.</param>
        public static IDisposable StartContext(T initial = default(T))
            => new Disposer(initial, _settings.Value);
    }
}
