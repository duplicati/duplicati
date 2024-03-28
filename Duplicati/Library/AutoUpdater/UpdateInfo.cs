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

namespace Duplicati.Library.AutoUpdater
{
    /// <summary>
    /// The different kinds of releases supported
    /// </summary>
    public enum ReleaseType
    {
        Unknown,
        Stable,
        Beta,
        Experimental,
        Canary,
        Nightly,
        Debug
    }

    public enum UpdateSeverity
    {
        None,
        Low,
        Normal,
        High,
        Critical
    }

    public class UpdateInfo
    {
        public string UpdateFromV1Url;
        public string Displayname;
        public string Version;
        public DateTime ReleaseTime;
        public string ReleaseType;
        public string UpdateSeverity;
        public string ChangeInfo;
        public long CompressedSize;
        public long UncompressedSize;
        public string SHA256;
        public string MD5;
        public string[] RemoteURLS;
        public FileEntry[] Files;

        public UpdateInfo Clone()
        {
            return (UpdateInfo)this.MemberwiseClone();
        }
    }
}

