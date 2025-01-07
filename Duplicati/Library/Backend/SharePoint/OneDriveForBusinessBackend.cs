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
