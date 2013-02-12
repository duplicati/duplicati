namespace Duplicati.Scheduler.Monitor.SQL
{
    partial class SQLConnectionDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.Windows.Forms.Label dataSourceLabel;
            System.Windows.Forms.Label passwordLabel;
            System.Windows.Forms.Label userIDLabel;
            System.Windows.Forms.Label label1;
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SQLConnectionDialog));
            this.integratedSecurityCheckBox = new System.Windows.Forms.CheckBox();
            this.passwordTextBox = new System.Windows.Forms.TextBox();
            this.userIDTextBox = new System.Windows.Forms.TextBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.SavePwCheckBox = new System.Windows.Forms.CheckBox();
            this.CanButton = new System.Windows.Forms.Button();
            this.OKButton = new System.Windows.Forms.Button();
            this.TestButton = new System.Windows.Forms.Button();
            this.CatalogTextBox = new System.Windows.Forms.TextBox();
            this.DataSourceComboBox = new System.Windows.Forms.ComboBox();
            dataSourceLabel = new System.Windows.Forms.Label();
            passwordLabel = new System.Windows.Forms.Label();
            userIDLabel = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // dataSourceLabel
            // 
            dataSourceLabel.AutoSize = true;
            dataSourceLabel.Location = new System.Drawing.Point(9, 13);
            dataSourceLabel.Name = "dataSourceLabel";
            dataSourceLabel.Size = new System.Drawing.Size(110, 13);
            dataSourceLabel.TabIndex = 3;
            dataSourceLabel.Text = "Data Source (Server):";
            // 
            // passwordLabel
            // 
            passwordLabel.AutoSize = true;
            passwordLabel.Location = new System.Drawing.Point(13, 48);
            passwordLabel.Name = "passwordLabel";
            passwordLabel.Size = new System.Drawing.Size(56, 13);
            passwordLabel.TabIndex = 17;
            passwordLabel.Text = "Password:";
            // 
            // userIDLabel
            // 
            userIDLabel.AutoSize = true;
            userIDLabel.Location = new System.Drawing.Point(23, 22);
            userIDLabel.Name = "userIDLabel";
            userIDLabel.Size = new System.Drawing.Size(46, 13);
            userIDLabel.TabIndex = 23;
            userIDLabel.Text = "User ID:";
            // 
            // integratedSecurityCheckBox
            // 
            this.integratedSecurityCheckBox.Location = new System.Drawing.Point(12, 75);
            this.integratedSecurityCheckBox.Name = "integratedSecurityCheckBox";
            this.integratedSecurityCheckBox.Size = new System.Drawing.Size(133, 24);
            this.integratedSecurityCheckBox.TabIndex = 8;
            this.integratedSecurityCheckBox.Text = "Integrated security";
            this.integratedSecurityCheckBox.UseVisualStyleBackColor = true;
            this.integratedSecurityCheckBox.CheckedChanged += new System.EventHandler(this.integratedSecurityCheckBox_CheckedChanged);
            // 
            // passwordTextBox
            // 
            this.passwordTextBox.Location = new System.Drawing.Point(75, 45);
            this.passwordTextBox.Name = "passwordTextBox";
            this.passwordTextBox.Size = new System.Drawing.Size(163, 20);
            this.passwordTextBox.TabIndex = 18;
            this.passwordTextBox.UseSystemPasswordChar = true;
            // 
            // userIDTextBox
            // 
            this.userIDTextBox.Location = new System.Drawing.Point(75, 19);
            this.userIDTextBox.Name = "userIDTextBox";
            this.userIDTextBox.Size = new System.Drawing.Size(163, 20);
            this.userIDTextBox.TabIndex = 24;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.SavePwCheckBox);
            this.groupBox1.Controls.Add(this.userIDTextBox);
            this.groupBox1.Controls.Add(userIDLabel);
            this.groupBox1.Controls.Add(this.passwordTextBox);
            this.groupBox1.Controls.Add(passwordLabel);
            this.groupBox1.Location = new System.Drawing.Point(35, 93);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(255, 100);
            this.groupBox1.TabIndex = 25;
            this.groupBox1.TabStop = false;
            // 
            // SavePwCheckBox
            // 
            this.SavePwCheckBox.AutoSize = true;
            this.SavePwCheckBox.Location = new System.Drawing.Point(75, 72);
            this.SavePwCheckBox.Name = "SavePwCheckBox";
            this.SavePwCheckBox.Size = new System.Drawing.Size(99, 17);
            this.SavePwCheckBox.TabIndex = 25;
            this.SavePwCheckBox.Text = "Save password";
            this.SavePwCheckBox.UseVisualStyleBackColor = true;
            // 
            // CanButton
            // 
            this.CanButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CanButton.CausesValidation = false;
            this.CanButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CanButton.Location = new System.Drawing.Point(247, 241);
            this.CanButton.Name = "CanButton";
            this.CanButton.Size = new System.Drawing.Size(75, 23);
            this.CanButton.TabIndex = 27;
            this.CanButton.Text = "Cancel";
            this.CanButton.UseVisualStyleBackColor = true;
            this.CanButton.Click += new System.EventHandler(this.CanButton_Click);
            // 
            // OKButton
            // 
            this.OKButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.OKButton.CausesValidation = false;
            this.OKButton.Location = new System.Drawing.Point(166, 241);
            this.OKButton.Name = "OKButton";
            this.OKButton.Size = new System.Drawing.Size(75, 23);
            this.OKButton.TabIndex = 28;
            this.OKButton.Text = "OK";
            this.OKButton.UseVisualStyleBackColor = true;
            this.OKButton.Click += new System.EventHandler(this.OKButton_Click);
            // 
            // TestButton
            // 
            this.TestButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.TestButton.CausesValidation = false;
            this.TestButton.Location = new System.Drawing.Point(12, 241);
            this.TestButton.Name = "TestButton";
            this.TestButton.Size = new System.Drawing.Size(75, 23);
            this.TestButton.TabIndex = 29;
            this.TestButton.Text = "Test";
            this.TestButton.UseVisualStyleBackColor = true;
            this.TestButton.Click += new System.EventHandler(this.TestButton_Click);
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(73, 39);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(46, 13);
            label1.TabIndex = 30;
            label1.Text = "Catalog:";
            // 
            // CatalogTextBox
            // 
            this.CatalogTextBox.Location = new System.Drawing.Point(125, 36);
            this.CatalogTextBox.Name = "CatalogTextBox";
            this.CatalogTextBox.Size = new System.Drawing.Size(197, 20);
            this.CatalogTextBox.TabIndex = 31;
            // 
            // DataSourceComboBox
            // 
            this.DataSourceComboBox.FormattingEnabled = true;
            this.DataSourceComboBox.Location = new System.Drawing.Point(125, 10);
            this.DataSourceComboBox.Name = "DataSourceComboBox";
            this.DataSourceComboBox.Size = new System.Drawing.Size(197, 21);
            this.DataSourceComboBox.TabIndex = 32;
            // 
            // SQLConnectionDialog
            // 
            this.AcceptButton = this.OKButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CanButton;
            this.ClientSize = new System.Drawing.Size(334, 276);
            this.Controls.Add(this.DataSourceComboBox);
            this.Controls.Add(label1);
            this.Controls.Add(this.CatalogTextBox);
            this.Controls.Add(this.TestButton);
            this.Controls.Add(this.OKButton);
            this.Controls.Add(this.CanButton);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.integratedSecurityCheckBox);
            this.Controls.Add(dataSourceLabel);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "SQLConnectionDialog";
            this.Text = "SQL Connection";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox integratedSecurityCheckBox;
        private System.Windows.Forms.TextBox passwordTextBox;
        private System.Windows.Forms.TextBox userIDTextBox;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button CanButton;
        private System.Windows.Forms.Button OKButton;
        private System.Windows.Forms.Button TestButton;
        private System.Windows.Forms.CheckBox SavePwCheckBox;
        private System.Windows.Forms.TextBox CatalogTextBox;
        private System.Windows.Forms.ComboBox DataSourceComboBox;
    }
}