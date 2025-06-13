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

namespace Duplicati.Library.Strings
{
    public static class OAuthHelper {
        public static string MissingAuthID(string url) { return LC.L(@"You need an AuthID. You can get it from: {0}", url); }
        public static string AuthorizationFailure(string message, string url) { return LC.L(@"Failed to authorize using the OAuth service: {0}. If the problem persists, try generating a new authid token from: {1}", message, url); }
        public static string UnexpectedError(System.Net.HttpStatusCode statuscode, string description) { return LC.L(@"Unexpected error code: {0} - {1}", statuscode, description); }
        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }
        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }
        public static string OverQuotaError { get { return LC.L(@"The OAuth service is currently over quota. Try again in a few hours"); } }
    }
}
