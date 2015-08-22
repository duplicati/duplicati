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
        /// True if the element is a symlink
        /// </summary>
        public bool symlink { get; set; }

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
