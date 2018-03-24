//  Copyright (C) 2016, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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
        private object m_prev;

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
            /* TODO-DNC
            m_prev = System.Runtime.Remoting.Messaging.CallContext.LogicalGetData(LocalizationService.LOGICAL_CONTEXT_KEY);
            System.Runtime.Remoting.Messaging.CallContext.LogicalSetData(LocalizationService.LOGICAL_CONTEXT_KEY, ci.Name);
            */
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
                /* TODO-DNC
                System.Runtime.Remoting.Messaging.CallContext.LogicalSetData(LocalizationService.LOGICAL_CONTEXT_KEY, m_prev);
                */
                m_isDisposed = true;
            }
        }
    }
}
