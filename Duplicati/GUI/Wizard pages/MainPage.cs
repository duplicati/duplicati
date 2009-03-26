#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages
{
    public partial class MainPage : WizardControl
    {
        WizardSettingsWrapper m_wrapper;

        public MainPage()
            : base("Welcome to the Duplicati Wizard", "Please select the action you want to perform below")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(MainPage_PageEnter);
            base.PageLeave += new PageChangeHandler(MainPage_PageLeave);
            base.PageDisplay += new PageChangeHandler(MainPage_PageDisplay);
        }

        void MainPage_PageDisplay(object sender, PageChangedArgs args)
        {
            //Skip this, as there is only one valid option
            if (Program.DataConnection.GetObjects<Datamodel.Schedule>().Length == 0)
            {
                CreateNew.Checked = true;
                try { m_owner.NextButton.PerformClick(); }
                catch { }
            }
        }

        void MainPage_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            UpdateButtonState();

            this.Controls.Remove(ShowAdvanced);
            m_owner.ButtonPanel.Controls.Add(ShowAdvanced);
            ShowAdvanced.Top = m_owner.CancelButton.Top;
            ShowAdvanced.Left = m_owner.ButtonPanel.Width - m_owner.CancelButton.Right;
            ShowAdvanced.Visible = false; //true;
            args.TreatAsLast = false;
        }

        void MainPage_PageLeave(object sender, PageChangedArgs args)
        {
            if (CreateNew.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Add;
            else if (Edit.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Edit;
            else if (Restore.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Restore;
            else if (Backup.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.RunNow;
            else if (Remove.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Remove;
            else
            {
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Unknown;
                args.NextPage = null;
                args.Cancel = true;
                return;
            }

            m_owner.ButtonPanel.Controls.Remove(ShowAdvanced);
            this.Controls.Add(ShowAdvanced);
            ShowAdvanced.Visible = false;

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
            {
                args.NextPage = new Add_backup.SelectName();
                SetupDefaults();
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Add;
            }
            else
                args.NextPage = new SelectBackup();
        }

        private void UpdateButtonState()
        {
            if (m_owner != null)
                m_owner.NextButton.Enabled = CreateNew.Checked | Edit.Checked | Restore.Checked | Backup.Checked | Remove.Checked;
        }

        private void Radio_CheckedChanged(object sender, EventArgs e)
        {
            UpdateButtonState();
        }

        private void ShowAdvanced_Click(object sender, EventArgs e)
        {
            Program.ShowSetup();
            m_owner.Dialog.DialogResult = DialogResult.Cancel;
            m_owner.Dialog.Close();
        }

        /// <summary>
        /// The purpose of this function is to set the default
        /// settings on the new backup.
        /// </summary>
        private void SetupDefaults()
        {
            m_settings.Clear();

            ApplicationSettings appset = new ApplicationSettings(Program.DataConnection);
            if (appset.UseCommonPassword)
            {
                m_wrapper.BackupPassword = appset.CommonPassword;
                m_wrapper.GPGEncryption = appset.CommonPasswordUseGPG;
            }

            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.Load(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Program), "Backup defaults.xml"));

            System.Xml.XmlNode root = doc.SelectSingleNode("settings");

            List<System.Xml.XmlNode> nodes = new List<System.Xml.XmlNode>();

            if (root != null)
                foreach(System.Xml.XmlNode n in root.ChildNodes)
                    nodes.Add(n);

            //Load user supplied settings
            string filename = System.IO.Path.Combine(Application.StartupPath, "Backup defaults.xml");
            if (System.IO.File.Exists(filename))
            {
                doc.Load(filename);
                root = doc.SelectSingleNode("settings");
                if (root != null)
                    foreach (System.Xml.XmlNode n in root.ChildNodes)
                        nodes.Add(n);
            }

            foreach(System.Xml.XmlNode n in nodes)
                if (n.NodeType == System.Xml.XmlNodeType.Element)
                {
                    System.Reflection.PropertyInfo pi = m_wrapper.GetType().GetProperty(n.Name);
                    if (pi != null && pi.CanWrite)
                        if (pi.PropertyType == typeof(DateTime))
                            pi.SetValue(m_wrapper,Library.Core.Timeparser.ParseTimeInterval(n.InnerText, DateTime.Now.Date), null);
                        else
                            pi.SetValue(m_wrapper, Convert.ChangeType(n.InnerText, pi.PropertyType), null);
                }

        }

        private void RadioButton_DoubleClick(object sender, EventArgs e)
        {
            m_owner.NextButton.PerformClick();
        }
    }
}
