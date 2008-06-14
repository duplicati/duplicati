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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace System.Windows.Forms.Wizard
{
    public partial class Dialog : Form, IWizardForm
    {
        private List<IWizardControl> m_pages;
        private Image m_defaultImage;
        private string m_title;
        private IWizardControl m_currentPage;

        public event CancelEventHandler Finished;
        public event EventHandler Cancelled;

        public Dialog(string title)
            : this()
        {
            m_title = title;
        }

        public Dialog(Image defaultImage)
            : this()
        {
            m_defaultImage = defaultImage;
        }

        public Dialog(IEnumerable<IWizardControl> wizardpages)
            : this()
        {
            if (wizardpages != null)
                m_pages.AddRange(wizardpages);
        }

        public Dialog()
        {
            InitializeComponent();
            m_pages = new List<IWizardControl>();
            m_defaultImage = null;
        }

        public virtual string Title
        {
            get { return m_title; }
            set { m_title = value; this.Text = value; }
        }

        public virtual Image DefaultImage
        {
            get { return m_defaultImage; }
            set { m_defaultImage = value; }
        }

        public virtual List<IWizardControl> Pages
        {
            get { return m_pages; }
        }

        public virtual IWizardControl CurrentPage
        {
            get { return m_currentPage; }
            set { m_currentPage = value; DisplayPage(m_currentPage); }
        }

        public virtual void UpdateButtons()
        {
            if (Pages.Count == 0)
            {
                _NextButton.Enabled = false;
                _BackButton.Enabled = false;
                return;
            }

            _NextButton.Enabled = true;
            _BackButton.Enabled = m_currentPage != Pages[0];
            if (m_currentPage == Pages[m_pages.Count - 1])
                _NextButton.Text = "Finish";
            else
                _NextButton.Text = "Next >";
        }

        public virtual void UpdateDisplay()
        {
            UpdateButtons();
            DisplayPage(CurrentPage);
        }

        protected virtual void DisplayPage(IWizardControl page)
        {
            InfoPanel.Visible = !page.FullSize;
            TitleLabel.Text = page.Title;
            InfoLabel.Text = page.HelpText;
            PageIcon.Image = page.Image == null ? DefaultImage : page.Image;
            ContentPanel.Controls.Clear();
            ContentPanel.Controls.Add(page.Control);
            page.Control.Dock = DockStyle.Fill;
            page.Displayed(this);
        }

        private void BackBtn_Click(object sender, EventArgs e)
        {
            int pos = Pages.IndexOf(CurrentPage);
            if (pos > 0)
                CurrentPage = Pages[pos - 1];
        }

        private void NextBtn_Click(object sender, EventArgs e)
        {
            int pos = Pages.IndexOf(CurrentPage);
            if (pos >= 0)
                if (pos == Pages.Count - 1)
                {
                    if (Finished != null)
                    {
                        CancelEventArgs ce = new CancelEventArgs(false);
                        Finished(this, ce);
                        if (ce.Cancel)
                            return;
                    }

                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                    CurrentPage = Pages[pos + 1];

        }

        private void CancelBtn_Click(object sender, EventArgs e)
        {
            if (Cancelled != null)
                Cancelled(this, null);
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }


        #region IWizardForm Members

        public Button NextButton
        {
            get { return _NextButton; }
        }

        public Button BackButton
        {
            get { return _BackButton; }
        }

        Button IWizardForm.CancelButton
        {
            get { return _CancelButton; }
        }

        public Panel ButtonPanel
        {
            get { return _ButtonPanel; }
        }

        public Panel ContentPanel
        {
            get { return _ContentPanel; }
        }

        public Panel InfoPanel
        {
            get { return _InfoPanel; }
        }

        public PictureBox PageIcon
        {
            get { return _PageIcon; }
        }

        public Label InfoLabel
        {
            get { return _InfoLabel; }
        }

        public Label TitleLabel
        {
            get { return _TitleLabel; }
        }

        Dialog IWizardForm.Dialog
        {
            get { return this; }
        }

        #endregion
    }
}