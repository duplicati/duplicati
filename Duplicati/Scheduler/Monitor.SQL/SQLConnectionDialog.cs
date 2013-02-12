using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Scheduler.Monitor.SQL
{
    /// <summary>
    /// Provides a form to edit the SQL connection string
    /// </summary>
    public partial class SQLConnectionDialog : Form
    {
        /// <summary>
        /// Encrypted connection string in an XML file
        /// </summary>
        public string HomeConnectionXMLFile { get; set; }
        /// <summary>
        /// Resulting connection string
        /// </summary>
        public string ConnectionString { get; private set; }
        /// <summary>
        /// Builder used to parse/set the string
        /// </summary>
        private System.Data.SqlClient.SqlConnectionStringBuilder ConnectionStringBuilder;
        /// <summary>
        /// Creator
        /// </summary>
        /// <param name="aHomeConnectionXMLFile">Name of XML file to load/save</param>
        public SQLConnectionDialog(string aHomeConnectionXMLFile)
        {
            InitializeComponent();
            // Save the file nae, load the connectin string
            this.HomeConnectionXMLFile = aHomeConnectionXMLFile;
            this.DataSourceComboBox.Items.Add(Environment.MachineName + @"\SQLEXPRESS");
            this.ConnectionString = new Duplicati.Scheduler.Data.ConnectionFromXML().ConnectionString(aHomeConnectionXMLFile);
        }
        /// <summary>
        /// Form loaded, populate the controls
        /// </summary>
        protected override void  OnLoad(EventArgs e)
        {
 	        base.OnLoad(e);
            try
            {
                // Parse the string
                this.ConnectionStringBuilder = new System.Data.SqlClient.SqlConnectionStringBuilder(this.ConnectionString);
            }
            catch (Exception Ex)
            {
                // If error, say it, and clear the connection string
                MessageBox.Show(Ex.Message);
                this.ConnectionStringBuilder = new System.Data.SqlClient.SqlConnectionStringBuilder();
            }
            // Set the controls
            this.DataSourceComboBox.Text = this.ConnectionStringBuilder.DataSource;
            this.integratedSecurityCheckBox.Checked = this.ConnectionStringBuilder.IntegratedSecurity;
            this.userIDTextBox.Text = this.ConnectionStringBuilder.UserID;
            this.passwordTextBox.Text = this.ConnectionStringBuilder.Password;
            this.SavePwCheckBox.Checked = this.ConnectionStringBuilder.PersistSecurityInfo;
            this.CatalogTextBox.Text = this.ConnectionStringBuilder.InitialCatalog;
        }
        /// <summary>
        /// Integrated checkbox pressed, enable/disable the login stuff
        /// </summary>
        private void integratedSecurityCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            this.groupBox1.Enabled = !this.integratedSecurityCheckBox.Checked;
        }
        /// <summary>
        /// OK pressed, get the string from the controls and close
        /// </summary>
        private void OKButton_Click(object sender, EventArgs e)
        {
            // Check if all OK, also loads the string builder from the controls
            if (!Parse()) return;
            this.ConnectionString = this.ConnectionStringBuilder.ConnectionString;
            try
            {
                // Save the result
                new Duplicati.Scheduler.Data.ConnectionFromXML().SaveConnection(this.HomeConnectionXMLFile,
                    this.ConnectionString);
            }
            catch (Exception Ex)
            {
                MessageBox.Show("Error saving connection string: " + this.HomeConnectionXMLFile + ": " + Ex.Message, "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            this.DialogResult = DialogResult.OK;
            Close();
        }
        /// <summary>
        /// Gets the connection string builder from the controls
        /// </summary>
        /// <returns>True if no errors</returns>
        private bool Parse()
        {
            bool Result = false;
            try
            {
                this.ConnectionStringBuilder.DataSource = this.DataSourceComboBox.Text;
                this.ConnectionStringBuilder.InitialCatalog = this.CatalogTextBox.Text;
                this.ConnectionStringBuilder.IntegratedSecurity = this.integratedSecurityCheckBox.Checked;
                if (!this.integratedSecurityCheckBox.Checked)
                {
                    this.ConnectionStringBuilder.UserID = this.userIDTextBox.Text;
                    this.ConnectionStringBuilder.Password = this.passwordTextBox.Text;
                    this.ConnectionStringBuilder.PersistSecurityInfo = this.SavePwCheckBox.Checked;
                }
                Result = true;
            }
            catch (Exception Ex)
            {
                MessageBox.Show(Ex.Message);
            }
            return Result;
        }
        /// <summary>
        /// Test pressed, test the connection
        /// </summary>
        private void TestButton_Click(object sender, EventArgs e)
        {
            // Get the builder
            if (!Parse()) return;
            try
            {
                // Open a connection
                using (System.Data.SqlClient.SqlConnection oc = new System.Data.SqlClient.SqlConnection(
                    this.ConnectionStringBuilder.ConnectionString))
                    oc.Open();
                MessageBox.Show("Connection succeeded", "SUCCESS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception Ex)
            {
                MessageBox.Show("Error connecting: " + Ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        /// <summary>
        /// Cancel pressed, close
        /// </summary>
        private void CanButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
