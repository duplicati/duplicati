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

using System.Collections.Generic;

namespace Duplicati.Server.Serialization.Interface
{
    /// <summary>
    /// All settings for a single backup
    /// </summary>
    public interface IBackup
    {
        /// <summary>
        /// The backup ID
        /// </summary>
        string ID { get; set; }
        /// <summary>
        /// The backup name
        /// </summary>
        string Name { get; set; }
        /// <summary>
        /// The backup description
        /// </summary>
        string Description { get; set; }
        /// <summary>
        /// The backup tags
        /// </summary>
        string[] Tags { get; set; }
        /// <summary>
        /// The backup target url
        /// </summary>
        string TargetURL { get; set; }
        /// <summary>
        /// The path to the local database
        /// </summary>
        string DBPath { get; }

        /// <summary>
        /// The backup source folders and files
        /// </summary>
        string[] Sources { get; set; }

        /// <summary>
        /// The backup settings
        /// </summary>
        ISetting[] Settings { get; set; }

        /// <summary>
        /// The filters applied to the source files
        /// </summary>
        IFilter[] Filters { get; set; }

        /// <summary>
        /// The backup metadata
        /// </summary>
        IDictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// Gets a value indicating if this instance is not persisted to the database
        /// </summary>
        bool IsTemporary { get; }

        void SanitizeTargetUrl();

        void SanitizeSettings();
    }
}

