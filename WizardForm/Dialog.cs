#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
        private bool m_isLastPage = false;
        private bool m_isBack = false; //Is the page change a back event?
        private Dictionary<string, object> m_settings;

        //The path the user has chosen
        private Stack<IWizardControl> m_visited;

        public event CancelEventHandler Finished;
        public event CancelEventHandler Cancelled;
        public event PageChangeHandler NextPressed;
        public event PageChangeHandler BackPressed;

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
            m_settings = new Dictionary<string, object>();
            m_visited = new Stack<IWizardControl>();
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

        public virtual Dictionary<string, object> Settings { get { return m_settings; } }

        public virtual IWizardControl CurrentPage
        {
            get { return m_currentPage; }
            set 
            { 
                m_currentPage = value; 
                DisplayPage(m_currentPage); 
            }
        }

        public virtual void UpdateButtons()
        {
            /*if (Pages.Count == 0)
            {
                _NextButton.Enabled = false;
                _BackButton.Enabled = false;
                return;
            }*/

            _NextButton.Enabled = true;
            _BackButton.Enabled = m_visited.Count > 0;

            if (m_isLastPage)
                _NextButton.Text = Strings.Dialog.FinishButtonText;
            else
                _NextButton.Text = Strings.Dialog.NextButtonText;
        }

        public virtual void UpdateDisplay()
        {
            DisplayPage(CurrentPage);
            UpdateButtons();
        }

        protected virtual void DisplayPage(IWizardControl page)
        {
            PageChangedArgs args = new PageChangedArgs(this, this.Pages.IndexOf(page) == this.Pages.Count - 1, m_isBack ? PageChangedDirection.Back : PageChangedDirection.Next);

            page.Control.Visible = false;
            page.Enter(this, args);

            InfoPanel.Visible = !page.FullSize;
            TitleLabel.Text = page.Title;
            InfoLabel.Text = page.HelpText;
            PageIcon.Image = page.Image == null ? DefaultImage : page.Image;
            ContentPanel.Controls.Clear();
            ContentPanel.Controls.Add(page.Control);
            page.Control.Dock = DockStyle.Fill;
            m_isLastPage = args.TreatAsLast;

            //Not sure this works under Mono...
            //TODO: The scaling sucks...
            /*try { this.Icon = System.Drawing.Icon.FromHandle(new System.Drawing.Bitmap(PageIcon.Image).GetHicon()); }
            catch { }*/

            UpdateButtons();

            page.Control.Visible = true;
            page.Display(this, args);

        }

        private void RefocusElement(object sender, EventArgs e)
        {
            try { CurrentPage.Control.Focus(); }
            catch { }
        }

        private void BackBtn_Click(object sender, EventArgs e)
        {
            try
            {
                m_isBack = true;
                PageChangedArgs args = new PageChangedArgs(this, false, PageChangedDirection.Back);

                if (m_visited.Count > 0)
                    args.NextPage = m_visited.Pop();

                if (this.CurrentPage != null)
                    this.CurrentPage.Leave(this, args);

                if (BackPressed != null)
                    BackPressed(this, args);

                if (args.Cancel || args.NextPage == null)
                    return;

                m_isLastPage = args.TreatAsLast;
                CurrentPage = args.NextPage;
            }
            finally
            {
                m_isBack = false;
            }

            BeginInvoke(new EventHandler(RefocusElement), sender, e);
        }

        private void NextBtn_Click(object sender, EventArgs e)
        {
            m_isBack = false;
            IWizardControl nextpage = null;
            int pos = Pages.IndexOf(CurrentPage);
            if (pos >= 0 && pos < Pages.Count - 1)
                nextpage = Pages[pos + 1];

            PageChangedArgs args = new PageChangedArgs(this, Pages.IndexOf(nextpage) == Pages.Count - 1 && Pages.Count != 0, PageChangedDirection.Next);
            args.NextPage = nextpage;

            if (m_currentPage != null)
            {
                m_currentPage.Leave(this, args);
                if (args.Cancel)
                    return;
            }

            if (m_isLastPage)
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

                return;
            }

            if (NextPressed != null)
                NextPressed(this, args);
            if (args.Cancel || args.NextPage == null)
                return;

            if (CurrentPage != null)
                m_visited.Push(CurrentPage);
            m_isLastPage = args.TreatAsLast;
            UpdateButtons();

            CurrentPage = args.NextPage;

            BeginInvoke(new EventHandler(RefocusElement), sender, e);
        }

        private void CancelBtn_Click(object sender, EventArgs e)
        {
            if (Cancelled != null)
            {
                CancelEventArgs ce = new CancelEventArgs(false);
                Cancelled(this, ce);
                if (ce.Cancel)
                    return;
            }
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

        public GroupBox ContentPanel
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

        private void Dialog_Load(object sender, EventArgs e)
        {
            if (this.Pages.Count > 0)
                this.CurrentPage = Pages[0];
        }

        private void Dialog_KeyUp(object sender, KeyEventArgs e)
        {
            //Setting the CancelButton property of the form does not allow canceling the event
            if (e.KeyCode == Keys.Escape)
                this.CancelBtn_Click(null, null);
        }
    }
}