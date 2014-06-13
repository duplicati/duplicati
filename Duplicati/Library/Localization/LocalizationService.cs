//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
    /// Provides an entry point for localization, regardless of the underlying translation engine
    /// </summary>
    public static class LocalizationService
    {
        /// <summary>
        /// A default service
        /// </summary>
        private static ILocalizationService DefaultService = new MockLocalizationService();

        /// <summary>
        /// Gets a localization provider with the default language
        /// </summary>
        public static ILocalizationService Default { get { return Get(System.Globalization.CultureInfo.InvariantCulture); } }

        /// <summary>
        /// Gets a localization provider with the current language
        /// </summary>
        public static ILocalizationService Current { get { return Get(System.Globalization.CultureInfo.CurrentCulture); } }

        /// <summary>
        /// Gets a localization provider with the OS install language
        /// </summary>
        public static ILocalizationService Installed { get { return Get(System.Globalization.CultureInfo.InstalledUICulture); } }

        /// <summary>
        /// Gets a localization provider with the specified language
        /// </summary>
        public static ILocalizationService Get(System.Globalization.CultureInfo ci)
        {
            return DefaultService;
        }
    }
}

