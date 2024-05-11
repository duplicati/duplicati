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
using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class Rclone
    {
        public static string DisplayName { get { return LC.L(@"Rclone"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to Rclone."); } }
        public static string RcloneLocalRepoShort { get { return LC.L(@"Local repository"); } }
        public static string RcloneLocalRepoLong { get { return LC.L(@"Local repository for Rclone. Make sure it is configured as a local drive, as it needs access to the files generated by Duplicati."); } }
        public static string RcloneRemoteRepoShort { get { return LC.L(@"Remote repository"); } }
        public static string RcloneRemoteRepoLong { get { return LC.L(@"Remote repository for Rclone. This can be any of the backends provided by Rclone. More info available on https://rclone.org/."); } }
        public static string RcloneRemotePathShort { get { return LC.L(@"Remote path"); } }
        public static string RcloneRemotePathLong { get { return LC.L(@"Path on the Remote repository. "); } }
        public static string RcloneOptionRcloneShort { get { return LC.L(@"Rclone options."); } }
        public static string RcloneOptionRcloneLong { get { return LC.L(@"Options will be transferred to rclone."); } }
        public static string RcloneExecutableShort { get { return LC.L(@"Rclone executable"); } }
        public static string RcloneExecutableLong { get { return LC.L(@"Full path to the rclone executable. Only needed if it's not in your path."); } }

    }
}
