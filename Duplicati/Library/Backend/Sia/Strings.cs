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
using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class Sia
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to Sia."); } }
        public static string DisplayName { get { return LC.L(@"Sia Decentralized Cloud"); } }
        public static string SiaPathDescriptionLong { get { return LC.L(@"Set the target path. Example: /backup"); } }
        public static string SiaPathDescriptionShort { get { return LC.L(@"Backup path"); } }
        public static string SiaPasswordLong { get { return LC.L(@"Supply a password for Sia server."); } }
        public static string SiaPasswordShort { get { return LC.L(@"Sia password"); } }
        public static string SiaRedundancyDescriptionLong { get { return LC.L(@"The minimum value for redundancy is 1.0."); } }
        public static string SiaRedundancyDescriptionShort { get { return LC.L(@"Set the minimum redundancy"); } }
    }
}
