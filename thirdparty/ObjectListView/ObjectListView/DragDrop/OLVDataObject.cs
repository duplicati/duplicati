/*
 * OLVDataObject.cs - An OLE DataObject that knows how to convert rows of an OLV to text and HTML
 *
 * Author: Phillip Piper
 * Date: 2011-03-29 3:34PM
 *
 * Change log:
 * 2011-03-29   JPP  - Initial version
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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BrightIdeasSoftware {
    
    /// <summary>
    /// A data transfer object that knows how to transform a list of model
    /// objects into a text and HTML representation.
    /// </summary>
    public class OLVDataObject : DataObject {
        #region Life and death

        /// <summary>
        /// Create a data object from the selected objects in the given ObjectListView
        /// </summary>
        /// <param name="olv">The source of the data object</param>
        public OLVDataObject(ObjectListView olv)
            : this(olv, olv.SelectedObjects) {
        }

        /// <summary>
        /// Create a data object which operates on the given model objects 
        /// in the given ObjectListView
        /// </summary>
        /// <param name="olv">The source of the data object</param>
        /// <param name="modelObjects">The model objects to be put into the data object</param>
        public OLVDataObject(ObjectListView olv, IList modelObjects) {
            this.objectListView = olv;
            this.modelObjects = modelObjects;
            this.includeHiddenColumns = olv.IncludeHiddenColumnsInDataTransfer;
            this.includeColumnHeaders = olv.IncludeColumnHeadersInCopy;
            this.CreateTextFormats();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets whether hidden columns will also be included in the text
        /// and HTML representation. If this is false, only visible columns will
        /// be included.
        /// </summary>
        public bool IncludeHiddenColumns {
            get { return includeHiddenColumns; }
        }
        private bool includeHiddenColumns;

        /// <summary>
        /// Gets or sets whether column headers will also be included in the text
        /// and HTML representation.
        /// </summary>
        public bool IncludeColumnHeaders {
            get { return includeColumnHeaders; }
        }
        private bool includeColumnHeaders;

        /// <summary>
        /// Gets the ObjectListView that is being used as the source of the data
        /// </summary>
        public ObjectListView ListView {
            get { return objectListView; }
        }
        private ObjectListView objectListView;

        /// <summary>
        /// Gets the model objects that are to be placed in the data object
        /// </summary>
        public IList ModelObjects {
            get { return modelObjects; }
        }
        private IList modelObjects = new ArrayList();

        #endregion

        /// <summary>
        /// Put a text and HTML representation of our model objects
        /// into the data object.
        /// </summary>
        public void CreateTextFormats() {
            IList<OLVColumn> columns = this.IncludeHiddenColumns ? this.ListView.AllColumns : this.ListView.ColumnsInDisplayOrder;

            // Build text and html versions of the selection
            StringBuilder sbText = new StringBuilder();
            StringBuilder sbHtml = new StringBuilder("<table>");

            // Include column headers
            if (includeColumnHeaders) {
                sbHtml.Append("<tr><td>");
                foreach (OLVColumn col in columns) {
                    if (col != columns[0]) {
                        sbText.Append("\t");
                        sbHtml.Append("</td><td>");
                    }
                    string strValue = col.Text;
                    sbText.Append(strValue);
                    sbHtml.Append(strValue); //TODO: Should encode the string value
                }
                sbText.AppendLine();
                sbHtml.AppendLine("</td></tr>");
            }

            foreach (object modelObject in this.ModelObjects) {
                sbHtml.Append("<tr><td>");
                foreach (OLVColumn col in columns) {
                    if (col != columns[0]) {
                        sbText.Append("\t");
                        sbHtml.Append("</td><td>");
                    }
                    string strValue = col.GetStringValue(modelObject);
                    sbText.Append(strValue);
                    sbHtml.Append(strValue); //TODO: Should encode the string value
                }
                sbText.AppendLine();
                sbHtml.AppendLine("</td></tr>");
            }
            sbHtml.AppendLine("</table>");

            // Put both the text and html versions onto the clipboard.
            // For some reason, SetText() with UnicodeText doesn't set the basic CF_TEXT format,
            // but using SetData() does.
            //this.SetText(sbText.ToString(), TextDataFormat.UnicodeText);
            this.SetData(sbText.ToString());
            this.SetText(ConvertToHtmlFragment(sbHtml.ToString()), TextDataFormat.Html);
        }

        /// <summary>
        /// Make a HTML representation of our model objects
        /// </summary>
        public string CreateHtml() {
            IList<OLVColumn> columns = this.ListView.ColumnsInDisplayOrder;

            // Build html version of the selection
            StringBuilder sbHtml = new StringBuilder("<table>");

            foreach (object modelObject in this.ModelObjects) {
                sbHtml.Append("<tr><td>");
                foreach (OLVColumn col in columns) {
                    if (col != columns[0]) {
                        sbHtml.Append("</td><td>");
                    }
                    string strValue = col.GetStringValue(modelObject);
                    sbHtml.Append(strValue); //TODO: Should encode the string value
                }
                sbHtml.AppendLine("</td></tr>");
            }
            sbHtml.AppendLine("</table>");

            return sbHtml.ToString();
        }

        /// <summary>
        /// Convert the fragment of HTML into the Clipboards HTML format.
        /// </summary>
        /// <remarks>The HTML format is found here http://msdn2.microsoft.com/en-us/library/aa767917.aspx
        /// </remarks>
        /// <param name="fragment">The HTML to put onto the clipboard. It must be valid HTML!</param>
        /// <returns>A string that can be put onto the clipboard and will be recognized as HTML</returns>
        private string ConvertToHtmlFragment(string fragment) {
            // Minimal implementation of HTML clipboard format
            string source = "http://www.codeproject.com/KB/list/ObjectListView.aspx";

            const String MARKER_BLOCK =
                "Version:1.0\r\n" +
                "StartHTML:{0,8}\r\n" +
                "EndHTML:{1,8}\r\n" +
                "StartFragment:{2,8}\r\n" +
                "EndFragment:{3,8}\r\n" +
                "StartSelection:{2,8}\r\n" +
                "EndSelection:{3,8}\r\n" +
                "SourceURL:{4}\r\n" +
                "{5}";

            int prefixLength = String.Format(MARKER_BLOCK, 0, 0, 0, 0, source, "").Length;

            const String DEFAULT_HTML_BODY =
                "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">" +
                "<HTML><HEAD></HEAD><BODY><!--StartFragment-->{0}<!--EndFragment--></BODY></HTML>";

            string html = String.Format(DEFAULT_HTML_BODY, fragment);
            int startFragment = prefixLength + html.IndexOf(fragment);
            int endFragment = startFragment + fragment.Length;

            return String.Format(MARKER_BLOCK, prefixLength, prefixLength + html.Length, startFragment, endFragment, source, html);
        }
    }
}
