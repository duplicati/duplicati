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

using System.Collections.Generic;

namespace Duplicati.Library.Backend.pCloud;

    /// <summary>
    /// Error codes/strings for pcloud. They will not be subject to translation so messages
    /// can be googled to help users diagnose issues.
    /// 
    /// </summary>
    internal static class pCloudErrorList
    {
        internal static readonly Dictionary<int, string> ErrorMessages = new()
        {
            { 1000, "Log in required." },
            { 1001, "No full path or name/folderid provided." },
            { 1004, "No fileid or path provided." },
            { 1005, "Unknown content-type requested." },
            { 2000, "Log in failed." },
            { 2002, "A component of parent directory does not exist." },
            { 2003, "Access denied. You do not have permissions to preform this operation." },
            { 2009, "File not found." },
            { 2010, "Invalid path." },
            { 2094, "Invalid 'access_token' provided. Please check you are using the correct server (eapi.pcloud.com for EU or api.pcloud.com for non EU)"},
            { 2011, "Requested speed limit too low, see minspeed for minimum." },
            { 4000, "Too many login tries from this IP address." },
            { 5002, "Internal error, no servers available. Try again later." }
        };
    }