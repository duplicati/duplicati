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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Interface for implementing callback based modules
    /// </summary>
    public interface IGenericCallbackModule : IGenericModule
    {
        /// <summary>
        /// Called when the operation starts
        /// </summary>
        /// <param name="operationname">The full name of the operation</param>
        void OnStart(string operationname, ref string remoteurl, ref string[] localpath);
        
        /// <summary>
        /// Called when the operation finishes
        /// </summary>
        /// <param name="result">The result object, if this derives from an exception, the operation failed</param>
        void OnFinish(object result);
    }
}
