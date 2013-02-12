/*
 * DesignSupport - Design time support for the various classes within ObjectListView
 *
 * Author: Phillip Piper
 * Date: 12/08/2009 8:36 PM
 *
 * Change log:
 * v2.3
 * 2009-08-12   JPP  - Initial version
 *
 * To do:
 *
 * Copyright (C) 2009 Phillip Piper
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
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing.Design;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Windows.Forms.Design;

using BrightIdeasSoftware;

namespace BrightIdeasSoftware.Design
{
    /*
    /// <summary>
    /// A specialised designer for the ObjectListView.
    /// </summary>
    /// <remarks>
    /// This is currently not enabled as the designer for ObjectListView, since we cannot
    /// duplicate all the functions of the base ListViewDesigner. The problem is that
    /// ListViewDesigner is internal to the .NET code, so we cannot use or subclass it.
    /// Here, I've duplicated what i can, but it is not fully working yet. In particular,
    /// I don't have an equivilent of HookChildWindows() method. 2009-09-12
    /// </remarks>
    internal class ObjectListViewDesigner : ListViewDesigner
    {
        protected override void PreFilterProperties(IDictionary properties) {
            // Always call the base PreFilterProperties implementation 
            // before you modify the properties collection.
            base.PreFilterProperties(properties);

            // I'd like to just remove the redundant properties, but that would
            // break backward compatibility. The deserialiser that handles the XXX.Designer.cs file
            // works off the designer, so even if the property exists in the class, the deserialiser will
            // throw an error if the associated designer removes that property.
            // So we shadow the unwanted properties, and give the replacement properties
            // non-browsable attributes so that they are hidden from the user

            List<string> unwantedProperties = new List<string>(new string[] { "BackgroundImage", "BackgroundImageTiled",
                "HotTracking", "HoverSelection", "LabelEdit", "VirtualListSize", "VirtualMode" });

            // Also hid Tooltip properties, since giving a tooltip to the control through the IDE
            // messes up the tooltip handling
            foreach (string propertyName in properties.Keys) {
                if (propertyName.StartsWith("ToolTip")) {
                    unwantedProperties.Add(propertyName);
                }
            }

            foreach (string unwantedProperty in unwantedProperties) {
                PropertyDescriptor propertyDesc = TypeDescriptor.CreateProperty(
                    typeof(ObjectListViewDesigner),
                    (PropertyDescriptor)properties[unwantedProperty],
                    new BrowsableAttribute(false));
                properties[unwantedProperty] = propertyDesc;
            }
        }
    }
    */
    /// <summary>
    /// This class works in conjunction with the OLVColumns property to allow OLVColumns
    /// to be added to the ObjectListView.
    /// </summary>
    public class OLVColumnCollectionEditor : System.ComponentModel.Design.CollectionEditor
    {
        /// <summary>
        /// Create a OLVColumnCollectionEditor
        /// </summary>
        /// <param name="t"></param>
        public OLVColumnCollectionEditor(Type t)
            : base(t) {
        }

        /// <summary>
        /// What type of object does this editor create?
        /// </summary>
        /// <returns></returns>
        protected override Type CreateCollectionItemType() {
            return typeof(OLVColumn);
        }

        /// <summary>
        /// Edit a given value
        /// </summary>
        /// <param name="context"></param>
        /// <param name="provider"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value) {
            // Figure out which ObjectListView we are working on. This should be the Instance of the context.
            ObjectListView olv = null;
            if (context != null)
                olv = context.Instance as ObjectListView;

            if (olv == null) {
                //THINK: Can this ever happen?
                System.Diagnostics.Debug.WriteLine("context.Instance was NOT an ObjectListView");

                // Hack to figure out which ObjectListView we are working on
                ListView.ColumnHeaderCollection cols = (ListView.ColumnHeaderCollection)value;
                if (cols.Count == 0) {
                    cols.Add(new OLVColumn());
                    olv = (ObjectListView)cols[0].ListView;
                    cols.Clear();
                    olv.AllColumns.Clear();
                } else
                    olv = (ObjectListView)cols[0].ListView;
            }

            // Edit all the columns, not just the ones that are visible
            base.EditValue(context, provider, olv.AllColumns);

            // Calculate just the visible columns
            List<OLVColumn> newColumns = olv.GetFilteredColumns(View.Details);
            olv.Columns.Clear();
            olv.Columns.AddRange(newColumns.ToArray());

            return olv.Columns;
        }

        /// <summary>
        /// What text should be shown in the list for the given object?
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        protected override string GetDisplayText(object value) {
            OLVColumn col = value as OLVColumn;
            if (col == null || String.IsNullOrEmpty(col.AspectName))
                return base.GetDisplayText(value);

            return String.Format("{0} ({1})", base.GetDisplayText(value), col.AspectName);
        }
    }


    /// <summary>
    /// Control how the overlay is presented in the IDE
    /// </summary>
    internal class OverlayConverter : ExpandableObjectConverter
    {
        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) {
            if (destinationType == typeof(string))
                return true;
            else
                return base.CanConvertTo(context, destinationType);
        }

        public override object ConvertTo(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType) {
            if (destinationType == typeof(string)) {
                ImageOverlay imageOverlay = value as ImageOverlay;
                if (imageOverlay != null) {
                    if (imageOverlay.Image == null)
                        return "(none)";
                    else
                        return "(set)";
                }
                TextOverlay textOverlay = value as TextOverlay;
                if (textOverlay != null) {
                    if (String.IsNullOrEmpty(textOverlay.Text))
                        return "(none)";
                    else
                        return "(set)";
                }
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
    /*
    // Everything from this point to the end of the file is a hack to get around
    // the fact that .NET's ListViewDesigner is internal. Being internal means that we cannot
    // subclass it or even reference it as our base designer class. So what follows 
    // is disassembled from the .NET Dlls by Reflector. There are still some bits
    // we still can't do, so they are have been commented out.

    #region ListViewDesigner

    internal class ListViewDesigner : ControlDesigner
    {
        // Fields
        private DesignerActionListCollection _actionLists;
        private NativeMethods.HDHITTESTINFO hdrhit = new NativeMethods.HDHITTESTINFO();
        //private bool inShowErrorDialog;

        // Methods
        protected override bool GetHitTest(System.Drawing.Point point) {
            //return base.GetHitTest(point);

            ObjectListView component = (ObjectListView)base.Component;
            return (component.HeaderControl.ColumnIndexUnderCursor >= 0);

            //if (component.View == View.Details)
            //{
            //    Point point2 = this.Control.PointToClient(point);
            //    IntPtr handle = component.Handle;
            //    IntPtr ptr2 = NativeMethods.ChildWindowFromPointEx(handle, point2.X, point2.Y, 1);
            //    if ((ptr2 != IntPtr.Zero) && (ptr2 != handle))
            //    {
            //        IntPtr hWndTo = NativeMethods.SendMessage(handle, 0x101f, IntPtr.Zero, IntPtr.Zero);
            //        if (ptr2 == hWndTo)
            //        {
            //            NativeMethods.POINT pt = new NativeMethods.POINT();
            //            pt.x = point.X;
            //            pt.y = point.Y;
            //            NativeMethods.MapWindowPoints(IntPtr.Zero, hWndTo, pt, 1);
            //            this.hdrhit.pt_x = pt.x;
            //            this.hdrhit.pt_y = pt.y;
            //            NativeMethods.SendMessage(hWndTo, 0x1206, IntPtr.Zero, this.hdrhit);
            //            if (this.hdrhit.flags == 4)
            //            {
            //                return true;
            //            }
            //        }
            //    }
            //}
            //return false;
        }

        public override void Initialize(IComponent component) {
            ListView view = (ListView)component;
            this.OwnerDraw = view.OwnerDraw;
            view.OwnerDraw = false;
            view.UseCompatibleStateImageBehavior = false;
            base.AutoResizeHandles = true;
            base.Initialize(component);
            //if (view.View == View.Details) {
            //    base.HookChildHandles(this.Control.Handle);
            //}
        }

        protected override void PreFilterProperties(IDictionary properties) {
            PropertyDescriptor oldPropertyDescriptor = (PropertyDescriptor)properties["OwnerDraw"];
            if (oldPropertyDescriptor != null) {
                properties["OwnerDraw"] = TypeDescriptor.CreateProperty(typeof(ListViewDesigner), oldPropertyDescriptor, new Attribute[0]);
            }
            PropertyDescriptor descriptor2 = (PropertyDescriptor)properties["View"];
            if (descriptor2 != null) {
                properties["View"] = TypeDescriptor.CreateProperty(typeof(ListViewDesigner), descriptor2, new Attribute[0]);
            }
            base.PreFilterProperties(properties);
        }

        protected override void WndProc(ref Message m) {
            switch (m.Msg) {
                case 0x4e:
                case 0x204e: {
                    NativeMethods.NMHDR nmhdr = (NativeMethods.NMHDR)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.NMHDR));
                    if (nmhdr.code == -327) //NativeMethods.HDN_ENDTRACK)
                {
                        try {
                            ((IComponentChangeService)this.GetService(typeof(IComponentChangeService))).OnComponentChanged(base.Component, null, null, null);
                        }
                        catch (InvalidOperationException) {
                            //if (!this.inShowErrorDialog)
                            //{
                            //    IUIService service = (IUIService) base.Component.Site.GetService(typeof(IUIService));
                            //    this.inShowErrorDialog = true;
                            //    try
                            //    {
                            //        DataGridViewDesigner.ShowErrorDialog(service, exception, (ListView) base.Component);
                            //    }
                            //    finally
                            //    {
                            //        this.inShowErrorDialog = false;
                            //    }
                            //}
                            return;
                        }
                    }
                    break;
                }
            }
            base.WndProc(ref m);
        }

        // Properties
        public override DesignerActionListCollection ActionLists {
            get {
                if (this._actionLists == null) {
                    this._actionLists = new DesignerActionListCollection();
                    this._actionLists.Add(new ListViewActionList(this));
                }
                return this._actionLists;
            }
        }

        public override ICollection AssociatedComponents {
            get {
                ObjectListView control = this.Control as ObjectListView;
                if (control != null) {
                    return control.AllColumns;
                }
                return base.AssociatedComponents;
            }
        }

        private bool OwnerDraw {
            get { return (bool)base.ShadowProperties["OwnerDraw"]; }
            set { base.ShadowProperties["OwnerDraw"] = value;  }
        }

        private View View {
            get { return ((ListView)base.Component).View; }
            set { ((ListView)base.Component).View = value;
                //if (value == View.Details) {
                //    base.HookChildHandles(this.Control.Handle);
                //}
            }
        }
    }

    internal class ListViewActionList : DesignerActionList
    {
        // Fields
        private ComponentDesigner _designer;

        // Methods
        public ListViewActionList(ComponentDesigner designer)
            : base(designer.Component) {
            this._designer = designer;
        }

        public override DesignerActionItemCollection GetSortedActionItems() {
            DesignerActionItemCollection items = new DesignerActionItemCollection();
            //items.Add(new DesignerActionMethodItem(this, "InvokeItemsDialog", "ListViewActionListEditItemsDisplayName", "PropertiesCategoryName", "ListViewActionListEditItemsDescription", true));
            items.Add(new DesignerActionMethodItem(this, "InvokeColumnsDialog", "Edit Columns", "Properties", "Edit the columns of this ObjectListView", true));
            //items.Add(new DesignerActionMethodItem(this, "InvokeGroupsDialog", "ListViewActionListEditGroupsDisplayName", "PropertiesCategoryName", "ListViewActionListEditGroupsDescription", true));
            items.Add(new DesignerActionPropertyItem("View", "View", "Properties", "View"));
            items.Add(new DesignerActionPropertyItem("SmallImageList", "Small Image List", "Properties", "Small Image List"));
            items.Add(new DesignerActionPropertyItem("LargeImageList", "Large Image List", "Properties", "Large Image List"));
            return items;
        }

        public void InvokeColumnsDialog() {
            EditorServiceContext.EditValue(this._designer, base.Component, "Columns");
        }

        //public void InvokeGroupsDialog() {
        //    EditorServiceContext.EditValue(this._designer, base.Component, "Groups");
        //}

        //public void InvokeItemsDialog() {
        //    EditorServiceContext.EditValue(this._designer, base.Component, "Items");
        //}

        // Properties
        public ImageList LargeImageList {
            get {
                return ((ObjectListView)base.Component).LargeImageList;
            }
            set {
                TypeDescriptor.GetProperties(base.Component)["LargeImageList"].SetValue(base.Component, value);
            }
        }

        public ImageList SmallImageList {
            get {
                return ((ObjectListView)base.Component).SmallImageList;
            }
            set {
                TypeDescriptor.GetProperties(base.Component)["SmallImageList"].SetValue(base.Component, value);
            }
        }

        public View View {
            get {
                return ((ListView)base.Component).View;
            }
            set {
                TypeDescriptor.GetProperties(base.Component)["View"].SetValue(base.Component, value);
            }
        }
    }

    internal class EditorServiceContext : IWindowsFormsEditorService, ITypeDescriptorContext, IServiceProvider
    {
        // Fields
        private IComponentChangeService _componentChangeSvc;
        private ComponentDesigner _designer;
        private PropertyDescriptor _targetProperty;

        // Methods
        internal EditorServiceContext(ComponentDesigner designer) {
            this._designer = designer;
        }

        internal EditorServiceContext(ComponentDesigner designer, PropertyDescriptor prop) {
            this._designer = designer;
            this._targetProperty = prop;
            if (prop == null) {
                prop = TypeDescriptor.GetDefaultProperty(designer.Component);
                if ((prop != null) && typeof(ICollection).IsAssignableFrom(prop.PropertyType)) {
                    this._targetProperty = prop;
                }
            }
        }

        internal EditorServiceContext(ComponentDesigner designer, PropertyDescriptor prop, string newVerbText)
            : this(designer, prop) {
            this._designer.Verbs.Add(new DesignerVerb(newVerbText, new EventHandler(this.OnEditItems)));
        }

        public static object EditValue(ComponentDesigner designer, object objectToChange, string propName) {
            PropertyDescriptor prop = TypeDescriptor.GetProperties(objectToChange)[propName];
            EditorServiceContext context = new EditorServiceContext(designer, prop);
            UITypeEditor editor = prop.GetEditor(typeof(UITypeEditor)) as UITypeEditor;
            object obj2 = prop.GetValue(objectToChange);
            object obj3 = editor.EditValue(context, context, obj2);
            if (obj3 != obj2) {
                try {
                    prop.SetValue(objectToChange, obj3);
                }
                catch (CheckoutException) {
                }
            }
            return obj3;
        }

        private void OnEditItems(object sender, EventArgs e) {
            object component = this._targetProperty.GetValue(this._designer.Component);
            if (component != null) {
                CollectionEditor editor = TypeDescriptor.GetEditor(component, typeof(UITypeEditor)) as CollectionEditor;
                if (editor != null) {
                    editor.EditValue(this, this, component);
                }
            }
        }

        void ITypeDescriptorContext.OnComponentChanged() {
            this.ChangeService.OnComponentChanged(this._designer.Component, this._targetProperty, null, null);
        }

        bool ITypeDescriptorContext.OnComponentChanging() {
            try {
                this.ChangeService.OnComponentChanging(this._designer.Component, this._targetProperty);
            }
            catch (CheckoutException exception) {
                if (exception != CheckoutException.Canceled) {
                    throw;
                }
                return false;
            }
            return true;
        }

        object IServiceProvider.GetService(Type serviceType) {
            if ((serviceType == typeof(ITypeDescriptorContext)) || (serviceType == typeof(IWindowsFormsEditorService))) {
                return this;
            }
            if (this._designer.Component.Site != null) {
                return this._designer.Component.Site.GetService(serviceType);
            }
            return null;
        }

        void IWindowsFormsEditorService.CloseDropDown() {
        }

        void IWindowsFormsEditorService.DropDownControl(Control control) {
        }

        DialogResult IWindowsFormsEditorService.ShowDialog(Form dialog) {
            IUIService service = (IUIService)((IServiceProvider)this).GetService(typeof(IUIService));
            if (service != null) {
                return service.ShowDialog(dialog);
            }
            return dialog.ShowDialog(this._designer.Component as IWin32Window);
        }

        // Properties
        private IComponentChangeService ChangeService {
            get {
                if (this._componentChangeSvc == null) {
                    this._componentChangeSvc = (IComponentChangeService)((IServiceProvider)this).GetService(typeof(IComponentChangeService));
                }
                return this._componentChangeSvc;
            }
        }

        IContainer ITypeDescriptorContext.Container {
            get {
                if (this._designer.Component.Site != null) {
                    return this._designer.Component.Site.Container;
                }
                return null;
            }
        }

        object ITypeDescriptorContext.Instance {
            get {
                return this._designer.Component;
            }
        }

        PropertyDescriptor ITypeDescriptorContext.PropertyDescriptor {
            get {
                return this._targetProperty;
            }
        }
    }

    #endregion
    */
}
