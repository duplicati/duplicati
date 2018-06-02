//  Copyright (C) 2015, The Duplicati Team

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

namespace Duplicati.Library.Localization.Short
{
    /// <summary>
    /// Localization using the default (invariant) culture
    /// </summary>
    public static class LD
    {
        /// <summary>
        /// The instance for translation
        /// </summary>
        private static readonly ILocalizationService LS = LocalizationService.Invariant;

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

    /// <summary>
    /// Localization using the current culture
    /// </summary>
    public static class LC
    {
        /// <summary>
        /// The instance for translation
        /// </summary>
        private static ILocalizationService LS = LocalizationService.Current;

        /// <summary>
        /// Sets the culture
        /// </summary>
        /// <param name="ci">CultureInfo</param>
        public static void setCulture(System.Globalization.CultureInfo ci)
        {
            LS = LocalizationService.Get(ci);
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

    /// <summary>
    /// Localization using the OS install culture
    /// </summary>
    public static class LI
    {
        /// <summary>
        /// The instance for translation
        /// </summary>
        private static readonly ILocalizationService LS = LocalizationService.Installed;

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

