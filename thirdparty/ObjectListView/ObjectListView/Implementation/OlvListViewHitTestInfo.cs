/*
 * OlvListViewHitTestInfo - All information gathered during a OlvHitTest() operation
 *
 * Author: Phillip Piper
 * Date: 31-March-2011 5:53 pm
 *
 * Change log:
 * 2011-03-31  JPP  - Split into its own file
 * 
 * Copyright (C) 2011 Phillip Piper
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *
 * If you wish to use this code in a closed source application, please contact phillip_piper@bigfoot.com.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace BrightIdeasSoftware {

    /// <summary>
    /// An indication of where a hit was within ObjectListView cell
    /// </summary>
    public enum HitTestLocation {
        /// <summary>
        /// Nowhere
        /// </summary>
        Nothing,

        /// <summary>
        /// On the text
        /// </summary>
        Text,

        /// <summary>
        /// On the image
        /// </summary>
        Image,

        /// <summary>
        /// On the checkbox
        /// </summary>
        CheckBox,

        /// <summary>
        /// On the expand button (TreeListView)
        /// </summary>
        ExpandButton,

        /// <summary>
        /// in the cell but not in any more specific location
        /// </summary>
        InCell,

        /// <summary>
        /// UserDefined location1 (used for custom renderers)
        /// </summary>
        UserDefined
    }

    /// <summary>
    /// Instances of this class encapsulate the information gathered during a OlvHitTest()
    /// operation.
    /// </summary>
    /// <remarks>Custom renderers can use HitTestLocation.UserDefined and the UserData
    /// object to store more specific locations for use during event handlers.</remarks>
    public class OlvListViewHitTestInfo {
        /// <summary>
        /// Create a OlvListViewHitTestInfo
        /// </summary>
        /// <param name="hti"></param>
        public OlvListViewHitTestInfo(ListViewHitTestInfo hti) {
            this.item = (OLVListItem)hti.Item;
            this.subItem = (OLVListSubItem)hti.SubItem;
            this.location = hti.Location;

            switch (hti.Location) {
            case ListViewHitTestLocations.StateImage:
                this.HitTestLocation = HitTestLocation.CheckBox;
                break;
            case ListViewHitTestLocations.Image:
                this.HitTestLocation = HitTestLocation.Image;
                break;
            case ListViewHitTestLocations.Label:
                this.HitTestLocation = HitTestLocation.Text;
                break;
            default:
                this.HitTestLocation = HitTestLocation.Nothing;
                break;
            }
        }

        #region Public fields

        /// <summary>
        /// Where is the hit location?
        /// </summary>
        public HitTestLocation HitTestLocation;

        /// <summary>
        /// Custom renderers can use this information to supply more details about the hit location
        /// </summary>
        public Object UserData;

        #endregion

        #region Public read-only properties

        /// <summary>
        /// Gets the item that was hit
        /// </summary>
        public OLVListItem Item {
            get { return item; }
            internal set { item = value; }
        }
        private OLVListItem item;

        /// <summary>
        /// Gets the subitem that was hit
        /// </summary>
        public OLVListSubItem SubItem {
            get { return subItem; }
            internal set { subItem = value; }
        }
        private OLVListSubItem subItem;

        /// <summary>
        /// Gets the part of the subitem that was hit
        /// </summary>
        public ListViewHitTestLocations Location {
            get { return location; }
            internal set { location = value; }
        }
        private ListViewHitTestLocations location;

        /// <summary>
        /// Gets the ObjectListView that was tested
        /// </summary>
        public ObjectListView ListView {
            get {
                if (this.Item == null)
                    return null;
                else
                    return (ObjectListView)this.Item.ListView;
            }
        }

        /// <summary>
        /// Gets the model object that was hit
        /// </summary>
        public Object RowObject {
            get {
                if (this.Item == null)
                    return null;
                else
                    return this.Item.RowObject;
            }
        }

        /// <summary>
        /// Gets the index of the row under the hit point or -1
        /// </summary>
        public int RowIndex {
            get {
                if (this.Item == null)
                    return -1;
                else
                    return this.Item.Index;
            }
        }

        /// <summary>
        /// Gets the index of the column under the hit point
        /// </summary>
        public int ColumnIndex {
            get {
                if (this.Item == null || this.SubItem == null)
                    return -1;
                else
                    return this.Item.SubItems.IndexOf(this.SubItem);
            }
        }

        /// <summary>
        /// Gets the column that was hit
        /// </summary>
        public OLVColumn Column {
            get {
                int index = this.ColumnIndex;
                if (index < 0)
                    return null;
                else
                    return this.ListView.GetColumn(index);
            }
        }

        #endregion
    }
}
