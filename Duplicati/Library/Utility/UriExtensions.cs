// Copyright (C) 2026, The Duplicati Team
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
using Duplicati.Library.Interface;

#nullable enable

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Checks that a backend url is usable, for the backends that have moved from
    /// <see cref="RelaxedUri"/> to <see cref="System.Uri"/>.
    /// These live here rather than in the backends because the messages are built from
    /// <see cref="Strings.Uri"/> and <see cref="Utility.StripUrlUserInfo"/>, both of which
    /// are internal to this assembly.
    /// </summary>
    public static class UriExtensions
    {
        /// <summary>
        /// Throws if the url has no hostname.
        /// </summary>
        /// <param name="uri">The parsed url.</param>
        /// <param name="originalUrl">The url as it was given, used for the message.</param>
        /// <remarks>
        /// Throws the same exception with the same message as <see cref="RelaxedUri.RequireHost"/>,
        /// so a backend that moves to <see cref="System.Uri"/> keeps reporting this the way it did.
        /// </remarks>
        public static void RequireHost(this System.Uri uri, string originalUrl)
        {
            if (string.IsNullOrEmpty(uri.Host))
                throw new ArgumentException(Strings.Uri.NoHostname(Utility.StripUrlUserInfo(originalUrl)));
        }

        /// <summary>
        /// Throws if the url carries a fragment.
        /// </summary>
        /// <param name="uri">The parsed url.</param>
        /// <param name="originalUrl">The url as it was given, used for the message.</param>
        /// <remarks>
        /// A "#" starts a fragment, so everything after it is not part of the path. The relaxed
        /// parser had no fragment and kept those characters, which means accepting the url would
        /// quietly move the backup to a different folder. This is told to the user instead, so
        /// this one throws <see cref="UserInformationException"/> rather than matching
        /// <see cref="RequireHost"/>: there is no earlier behaviour to keep, and the user has
        /// something to fix.
        /// </remarks>
        public static void RequireNoFragment(this System.Uri uri, string originalUrl)
        {
            if (!string.IsNullOrEmpty(uri.Fragment))
                throw new UserInformationException(
                    Strings.Uri.FragmentNotAllowed(Utility.StripUrlUserInfo(originalUrl)), "UriHasFragment");
        }
    }
}
