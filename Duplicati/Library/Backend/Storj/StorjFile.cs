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
using Duplicati.Library.Interface;
using System;
using System.Linq;

namespace Duplicati.Library.Backend.Storj
{
    public class StorjFile : IFileEntry
    {
        public static readonly string STORJ_LAST_ACCESS = "DUPLICATI:LAST-ACCESS";
        public static readonly string STORJ_LAST_MODIFICATION = "DUPLICATI:LAST-MODIFICATION";
        public bool IsFolder { get; set; }

        public DateTime LastAccess { get; set; }

        public DateTime LastModification { get; set; }
        public DateTime Created { get; set; }

        public string Name { get; set; }

        public long Size { get; set; }

        public StorjFile()
        {

        }

        public StorjFile(uplink.NET.Models.Object tardigradeObject)
        {
            IsFolder = tardigradeObject.IsPrefix;
            var lastAccess = tardigradeObject.CustomMetadata.Entries.Where(e => e.Key == STORJ_LAST_ACCESS).FirstOrDefault();
            if (lastAccess != null && !string.IsNullOrEmpty(lastAccess.Value))
            {
                LastAccess = DateTime.Parse(lastAccess.Value);
            }
            else
            {
                LastAccess = DateTime.MinValue;
            }

            var lastMod = tardigradeObject.CustomMetadata.Entries.Where(e => e.Key == STORJ_LAST_MODIFICATION).FirstOrDefault();
            if (lastMod != null && !string.IsNullOrEmpty(lastMod.Value))
            {
                LastModification = DateTime.Parse(lastMod.Value);
            }
            else
            {
                LastModification = DateTime.MinValue;
            }

            Name = tardigradeObject.Key;
            Size = tardigradeObject.SystemMetadata.ContentLength;
        }
    }
}
