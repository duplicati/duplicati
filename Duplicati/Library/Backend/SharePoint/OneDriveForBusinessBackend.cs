#region Disclaimer / License
// Copyright (C) 2016, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Shadow class above SharePointBackend to provide an extra protocol key for OneDrive for Business.
    /// Right now, OneDrive for Business *IS* SharePoint. But if MS turns funny sometimes in the future and 
    /// moves away from SharePoint as OD4B base, this allows to re-implement it without breaking existing
    /// configurations...
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    // This constructor is needed by the BackendLoader.
    public class OneDriveForBusinessBackend : SharePointBackend
    {
        public override string ProtocolKey
        {
            get { return "od4b"; }
        }

        public override string DisplayName
        {
            get { return Strings.OneDriveForBusiness.DisplayName; }
        }

        public override string Description
        {
            get { return Strings.OneDriveForBusiness.Description; }
        }

        public OneDriveForBusinessBackend()
            :base ()
        { }

        public OneDriveForBusinessBackend(string url, Dictionary<string, string> options)
            : base(url, options)
        { }


    }
}
