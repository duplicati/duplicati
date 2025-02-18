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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Duplicati.Library.Backend
{
    [SupportedOSPlatform("windows")]
    internal class Win32
    {
        [DllImport("mpr.dll")]
        private static extern int WNetAddConnection2(ref NETRESOURCE netResource,
           string password, string username, int flags);

        [DllImport("mpr.dll")]
        public static extern int WNetCancelConnection2(string sharename, int dwFlags, int fForce);

        private struct NETRESOURCE
        {
            public ResourceScope dwScope;
            public ResourceType dwType;
            public ResourceDisplayType dwDisplayType;
            public ResourceUsage dwUsage;
            public string LocalName;
            public string RemoteName;
            public string Comment;
            public string Provider;
        }

        private enum ResourceScope : int
        {
            RESOURCE_CONNECTED = 1,
            RESOURCE_GLOBALNET,
            RESOURCE_REMEMBERED,
            RESOURCE_RECENT,
            RESOURCE_CONTEXT
        };

        private enum ResourceType : int
        {
            RESOURCETYPE_ANY,
            RESOURCETYPE_DISK,
            RESOURCETYPE_PRINT,
            RESOURCETYPE_RESERVED
        };

        private enum ResourceDisplayType : int
        {
            RESOURCEDISPLAYTYPE_GENERIC,
            RESOURCEDISPLAYTYPE_DOMAIN,
            RESOURCEDISPLAYTYPE_SERVER,
            RESOURCEDISPLAYTYPE_SHARE,
            RESOURCEDISPLAYTYPE_FILE,
            RESOURCEDISPLAYTYPE_GROUP,
            RESOURCEDISPLAYTYPE_NETWORK,
            RESOURCEDISPLAYTYPE_ROOT,
            RESOURCEDISPLAYTYPE_SHAREADMIN,
            RESOURCEDISPLAYTYPE_DIRECTORY,
            RESOURCEDISPLAYTYPE_TREE,
            RESOURCEDISPLAYTYPE_NDSCONTAINER
        };

        private enum ResourceUsage : int
        {
            RESOURCEUSAGE_CONNECTABLE = 0x00000001,
            RESOURCEUSAGE_CONTAINER = 0x00000002,
            RESOURCEUSAGE_NOLOCALDEVICE = 0x00000004,
            RESOURCEUSAGE_SIBLING = 0x00000008,
            RESOURCEUSAGE_ATTACHED = 0x00000010,
            RESOURCEUSAGE_ALL = (RESOURCEUSAGE_CONNECTABLE | RESOURCEUSAGE_CONTAINER | RESOURCEUSAGE_ATTACHED),
        };

        private const int CONNECT_UPDATE_PROFILE = 0x1;

        internal static bool PreAuthenticate(string path, string username, string password, bool forceReauth)
        {
            //Strip it down from \\server\share\folder1\folder2\filename.extension to
            // \\server\share
            string minpath = path;
            if (!minpath.StartsWith("\\\\", StringComparison.Ordinal))
                return false;

            int first = minpath.IndexOf("\\", 2, StringComparison.Ordinal);
            if (first <= 0)
                return false;
            int next = minpath.IndexOf("\\", first + 1, StringComparison.Ordinal);
            if (next >= 0)
                minpath = minpath.Substring(0, next);

            //This only works on Windows, and probably not on Win95

            try
            {
                NETRESOURCE rsc = new NETRESOURCE();

                rsc.dwScope = ResourceScope.RESOURCE_GLOBALNET;
                rsc.dwType = ResourceType.RESOURCETYPE_DISK;
                rsc.dwDisplayType = ResourceDisplayType.RESOURCEDISPLAYTYPE_SHARE;
                rsc.dwUsage = ResourceUsage.RESOURCEUSAGE_CONNECTABLE;
                rsc.LocalName = null;
                rsc.RemoteName = minpath;

                // Forces close an existing network connection
                if (forceReauth)
                    WNetCancelConnection2(minpath, CONNECT_UPDATE_PROFILE, 1);

                int retCode = WNetAddConnection2(ref rsc, password, username, CONNECT_UPDATE_PROFILE);

                return retCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
