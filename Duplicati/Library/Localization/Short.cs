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
using System.Threading;

namespace Duplicati.Library.Localization.Short
{
    /// <summary>
    /// Localization using the current culture
    /// </summary>
    public static class LC
    {
        /// <summary>
        /// The instance for translation
        /// </summary>
        private static ILocalizationService LS
        {
            // Get up-to-date service (may be changed by temporary contexts)
            get => LocalizationService.Current;
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <returns>The localized string</returns>
        public static string L(string message)
        {
            return LS.Localize(message);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <returns>The localized string</returns>
        public static string L(string message, object arg0)
        {
            return LS.Localize(message, arg0);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <param name="arg1">The second argument</param>
        /// <returns>The localized string</returns>
        public static string L(string message, object arg0, object arg1)
        {
            return LS.Localize(message, arg0, arg1);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <param name="arg1">The second argument</param>
        /// <param name="arg2">The third argument</param>
        /// <returns>The localized string</returns>
        public static string L(string message, object arg0, object arg1, object arg2)
        {
            return LS.Localize(message, arg0, arg1, arg2);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="args">The arguments</param>
        /// <returns>The localized string</returns>
        public static string L(string message, params object[] args)
        {
            return LS.Localize(message, args);
        }
    }
}

