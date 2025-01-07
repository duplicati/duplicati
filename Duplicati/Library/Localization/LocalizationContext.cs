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

using Duplicati.Library.Logging;
using System;

namespace Duplicati.Library.Localization
{
    /// <summary>
    /// Helper class for choosing a temporary localization context
    /// </summary>
    internal class LocalizationContext : IDisposable
    {
        /// <summary>
        /// The previous context
        /// </summary>
        private readonly object m_prev;

        /// <summary>
        /// Flag to prevent double dispose
        /// </summary>
        private bool m_isDisposed = true;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Localization.LocalizationContext"/> class.
        /// </summary>
        /// <param name="ci">The localization to use.</param>
        public LocalizationContext(System.Globalization.CultureInfo ci)
        {
            m_prev = CallContext.GetData(LocalizationService.LOGICAL_CONTEXT_KEY) as string;
            CallContext.SetData(LocalizationService.LOGICAL_CONTEXT_KEY, ci.Name);

            m_isDisposed = false;
        }

        /// <summary>
        /// Releases all resource used by the <see cref="T:Duplicati.Library.Localization.LocalizationContext"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.Library.Localization.LocalizationContext"/>. The <see cref="Dispose"/> method leaves
        /// the <see cref="T:Duplicati.Library.Localization.LocalizationContext"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.Library.Localization.LocalizationContext"/> so the garbage collector can reclaim the
        /// memory that the <see cref="T:Duplicati.Library.Localization.LocalizationContext"/> was occupying.</remarks>
        public void Dispose()
        {
            if (!m_isDisposed)
            {
                CallContext.SetData(LocalizationService.LOGICAL_CONTEXT_KEY, m_prev);
                m_isDisposed = true;
            }
        }
    }
}
