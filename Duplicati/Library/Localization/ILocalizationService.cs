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
using System.Collections.Generic;

namespace Duplicati.Library.Localization
{
    public interface ILocalizationService
    {
        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <returns>The localized string</returns>
        string Localize(string message);

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <returns>The localized string</returns>
        string Localize(string message, object arg0);

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <param name="arg1">The second argument</param>
        /// <returns>The localized string</returns>
        string Localize(string message, object arg0, object arg1);

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <param name="arg1">The second argument</param>
        /// <param name="arg2">The third argument</param>
        /// <returns>The localized string</returns>
        string Localize(string message, object arg0, object arg1, object arg2);

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="args">The arguments</param>
        string Localize(string message, params object[] args);
    }
}

