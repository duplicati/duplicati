#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;

namespace System.Windows.Forms.Wizard
{
    public class WizardControl : IWizardControl
    {
        private Control m_control;
        private string m_title;
        private string m_helpText;
        private Image m_image;
        private bool m_fullSize;

        public WizardControl(Control control, string title, string helptext)
            : this(control, title, helptext, null)
        {
        }

        public WizardControl(Control control, string title, string helptext, Image image)
            : this(control, title, helptext, image, false)
        {
        }

        public WizardControl(Control control, string title, string helptext, Image image, bool fullsize)
        {
            m_control = control;
            m_title = title;
            m_helpText = helptext;
            m_image = image;
            m_fullSize = fullsize;
        }

        #region IWizardControl Members

        public Control Control
        {
            get { return m_control; }
        }

        public string Title
        {
            get { return m_title; }
        }

        public string HelpText
        {
            get { return m_helpText; }
        }

        public Image Image
        {
            get { return m_image; }
        }

        public bool FullSize
        {
            get { return m_fullSize; }
        }

        public virtual void Displayed(IWizardForm owner)
        {
        }

        #endregion
    }
}
