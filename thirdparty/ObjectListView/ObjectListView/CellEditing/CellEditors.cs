/*
 * CellEditors - Several slightly modified controls that are used as celleditors within ObjectListView.
 *
 * Author: Phillip Piper
 * Date: 20/10/2008 5:15 PM
 *
 * Change log:
 * v2.3
 * 2009-08-13   JPP  - Standardized code formatting
 * v2.2.1
 * 2008-01-18   JPP  - Added special handling for enums
 * 2008-01-16   JPP  - Added EditorRegistry
 * v2.0.1
 * 2008-10-20   JPP  - Separated from ObjectListView.cs
 * 
 * Copyright (C) 2006-2008 Phillip Piper
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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace BrightIdeasSoftware
{
    

    /// <summary>
    /// These items allow combo boxes to remember a value and its description.
    /// </summary>
    internal class ComboBoxItem
    {
        public ComboBoxItem(Object key, String description) {
            this.key = key;
            this.description = description;
        }
        private String description;

        public Object Key {
            get { return key; }
        }
        private Object key;

        public override string ToString() {
            return this.description;
        }
    } 

    //-----------------------------------------------------------------------
    // Cell editors
    // These classes are simple cell editors that make it easier to get and set
    // the value that the control is showing.
    // In many cases, you can intercept the CellEditStarting event to 
    // change the characteristics of the editor. For example, changing
    // the acceptable range for a numeric editor or changing the strings
    // that respresent true and false values for a boolean editor.

    /// <summary>
    /// This editor shows and auto completes values from the given listview column.
    /// </summary>
    [ToolboxItem(false)]
    public class AutoCompleteCellEditor : ComboBox
    {
        /// <summary>
        /// Create an AutoCompleteCellEditor
        /// </summary>
        /// <param name="lv"></param>
        /// <param name="column"></param>
        public AutoCompleteCellEditor(ObjectListView lv, OLVColumn column) {
            this.DropDownStyle = ComboBoxStyle.DropDown;

            Dictionary<String, bool> alreadySeen = new Dictionary<string, bool>();
            for (int i = 0; i < Math.Min(lv.GetItemCount(), 1000); i++) {
                String str = column.GetStringValue(lv.GetModelObject(i));
                if (!alreadySeen.ContainsKey(str)) {
                    this.Items.Add(str);
                    alreadySeen[str] = true;
                }
            }

            this.Sorted = true;
            this.AutoCompleteSource = AutoCompleteSource.ListItems;
            this.AutoCompleteMode = AutoCompleteMode.Append;
        }
    }

    /// <summary>
    /// This combo box is specialised to allow editing of an enum.
    /// </summary>
    internal class EnumCellEditor : ComboBox
    {
        public EnumCellEditor(Type type) {
            this.DropDownStyle = ComboBoxStyle.DropDownList;
            this.ValueMember = "Key";

            ArrayList values = new ArrayList();
            foreach (object value in Enum.GetValues(type))
                values.Add(new ComboBoxItem(value, Enum.GetName(type, value)));

            this.DataSource = values;
        }
    }

    /// <summary>
    /// This editor simply shows and edits integer values.
    /// </summary>
    internal class IntUpDown : NumericUpDown
    {
        public IntUpDown() {
            this.DecimalPlaces = 0;
            this.Minimum = -9999999;
            this.Maximum = 9999999;
        }

        new public int Value {
            get { return Decimal.ToInt32(base.Value); }
            set { base.Value = new Decimal(value); }
        }
    }

    /// <summary>
    /// This editor simply shows and edits unsigned integer values.
    /// </summary>
    internal class UintUpDown : NumericUpDown
    {
        public UintUpDown() {
            this.DecimalPlaces = 0;
            this.Minimum = 0;
            this.Maximum = 9999999;
        }

        new public uint Value {
            get { return Decimal.ToUInt32(base.Value); }
            set { base.Value = new Decimal(value); }
        }
    }

    /// <summary>
    /// This editor simply shows and edits boolean values.
    /// </summary>
    internal class BooleanCellEditor : ComboBox
    {
        public BooleanCellEditor() {
            this.DropDownStyle = ComboBoxStyle.DropDownList;
            this.ValueMember = "Key";

            ArrayList values = new ArrayList();
            values.Add(new ComboBoxItem(false, "False"));
            values.Add(new ComboBoxItem(true, "True"));

            this.DataSource = values;
        }
    }

    /// <summary>
    /// This editor simply shows and edits boolean values using a checkbox
    /// </summary>
    internal class BooleanCellEditor2 : CheckBox
    {
        public BooleanCellEditor2() {
        }

        public bool? Value {
            get {
                switch (this.CheckState) {
                    case CheckState.Checked: return true;
                    case CheckState.Indeterminate: return null;
                    case CheckState.Unchecked: 
                    default: return false;
                }
            }
            set {
                if (value.HasValue) 
                    this.CheckState = value.Value ? CheckState.Checked : CheckState.Unchecked;
                else
                    this.CheckState = CheckState.Indeterminate;
            }
        }

        public new HorizontalAlignment TextAlign {
            get {
                switch (this.CheckAlign) {
                    case ContentAlignment.MiddleRight: return HorizontalAlignment.Right;
                    case ContentAlignment.MiddleCenter: return HorizontalAlignment.Center;
                    case ContentAlignment.MiddleLeft: 
                    default: return HorizontalAlignment.Left;
                }
            }
            set {
                switch (value) {
                    case HorizontalAlignment.Left:
                        this.CheckAlign = ContentAlignment.MiddleLeft;
                        break;
                    case HorizontalAlignment.Center:
                        this.CheckAlign = ContentAlignment.MiddleCenter;
                        break;
                    case HorizontalAlignment.Right:
                        this.CheckAlign = ContentAlignment.MiddleRight;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// This editor simply shows and edits floating point values.
    /// </summary>
    /// <remarks>You can intercept the CellEditStarting event if you want
    /// to change the characteristics of the editor. For example, by increasing
    /// the number of decimal places.</remarks>
    internal class FloatCellEditor : NumericUpDown
    {
        public FloatCellEditor() {
            this.DecimalPlaces = 2;
            this.Minimum = -9999999;
            this.Maximum = 9999999;
        }

        new public double Value {
            get { return Convert.ToDouble(base.Value); }
            set { base.Value = Convert.ToDecimal(value); }
        }
    }
}
