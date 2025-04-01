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

namespace Duplicati.Server.Serializable
{
    /// <summary>
    /// Implementation of a ExtJS treenode-like class for easy JSON export
    /// </summary>
    public class TreeNode
    {
        /// <summary>
        /// The text displayed for the node
        /// </summary>
        public string text { get; set; }
        /// <summary>
        /// The node id
        /// </summary>
        public string id { get; set; }
        /// <summary>
        /// The class applied to the node
        /// </summary>
        public string cls { get; set; }
        /// <summary>
        /// The class applied to the icon
        /// </summary>
        public string iconCls { get; set; }
        /// <summary>
        /// True if the element should be checked
        /// </summary>
        public bool check { get; set; }
        /// <summary>
        /// True if the element is a leaf node
        /// </summary>
        public bool leaf { get; set; }
        /// <summary>
        /// Gets or sets the current path, if the item is a symbolic path
        /// </summary>
        public string resolvedpath { get; set; }
        /// <summary>
        /// True if the element is hidden
        /// </summary>
        public bool hidden { get; set; }
        /// <summary>
        /// True if the element has the system file attribute
        /// </summary>
        public bool systemFile { get; set; }
        /// <summary>
        /// True if the element is marked as temporary
        /// </summary>
        public bool temporary { get; set; }
        /// <summary>
        /// True if the element is a symlink
        /// </summary>
        public bool symlink { get; set; }
        /// <summary>
        /// Size of the file. -1 if directory or inaccessible
        /// </summary>
        public long fileSize { get; set; }

        /// <summary>
        /// Constructs a new TreeNode
        /// </summary>
        public TreeNode() 
        {
            this.cls = "folder";
            this.iconCls = "x-tree-icon-parent";
            this.check = false;
        }
    }
}
