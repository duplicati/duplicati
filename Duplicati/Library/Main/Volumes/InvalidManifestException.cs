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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Volumes
{
    [Serializable]
    public class InvalidManifestException : UserInformationException
    {
        private static string GetMessage(string fieldname, string value, string expected)
        {
            // Make a custom message if this is a version mismatch
            if (string.Equals(fieldname, "version", StringComparison.OrdinalIgnoreCase) || string.Equals(fieldname, "encoding", StringComparison.OrdinalIgnoreCase))
                return $"Invalid manifest detected, the field {fieldname} has value {value} but the value {expected} was expected.\nThe most likely cause for this issue is that you are attempting to read a backup created with a newer version of Duplicati.\nPlease upgrade your Duplicati installation to the latest version to resolve this issue.";

            return $"Invalid manifest detected, the field {fieldname} has value {value} but the value {expected} was expected. This could be a configuration issue, or be caused by mixing files from different backups.";
        }
        public InvalidManifestException(string fieldname, string value, string expected)
            : base(GetMessage(fieldname, value, expected), "InvalidManifest")
        {
        }

        public InvalidManifestException(string message)
            : base(message, "InvalidManifest")
        {
        }
    }
}
