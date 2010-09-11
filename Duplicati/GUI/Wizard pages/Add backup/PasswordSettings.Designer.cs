namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class PasswordSettings
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PasswordSettings));
            this.EnablePassword = new System.Windows.Forms.CheckBox();
            this.PasswordHelptext = new System.Windows.Forms.Label();
            this.UseSettingsAsDefault = new System.Windows.Forms.CheckBox();
            this.EncryptionModuleLabel = new System.Windows.Forms.Label();
            this.EncryptionModule = new System.Windows.Forms.ComboBox();
            this.GeneratePasswordButton = new System.Windows.Forms.Button();
            this.EncryptionControlContainer = new System.Windows.Forms.Panel();
            this.Password = new Duplicati.Winforms.Controls.PasswordControl();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.SuspendLayout();
            // 
            // EnablePassword
            // 
            resources.ApplyResources(this.EnablePassword, "EnablePassword");
            this.EnablePassword.Checked = true;
            this.EnablePassword.CheckState = System.Windows.Forms.CheckState.Checked;
            this.EnablePassword.Name = "EnablePassword";
            this.EnablePassword.UseVisualStyleBackColor = true;
            this.EnablePassword.CheckedChanged += new System.EventHandler(this.EnablePassword_CheckedChanged);
            // 
            // PasswordHelptext
            // 
            resources.ApplyResources(this.PasswordHelptext, "PasswordHelptext");
            this.PasswordHelptext.Name = "PasswordHelptext";
            // 
            // UseSettingsAsDefault
            // 
            resources.ApplyResources(this.UseSettingsAsDefault, "UseSettingsAsDefault");
            this.UseSettingsAsDefault.Name = "UseSettingsAsDefault";
            this.toolTip.SetToolTip(this.UseSettingsAsDefault, resources.GetString("UseSettingsAsDefault.ToolTip"));
            this.UseSettingsAsDefault.UseVisualStyleBackColor = true;
            // 
            // EncryptionModuleLabel
            // 
            resources.ApplyResources(this.EncryptionModuleLabel, "EncryptionModuleLabel");
            this.EncryptionModuleLabel.Name = "EncryptionModuleLabel";
            // 
            // EncryptionModule
            // 
            this.EncryptionModule.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.EncryptionModule.FormattingEnabled = true;
            resources.ApplyResources(this.EncryptionModule, "EncryptionModule");
            this.EncryptionModule.Name = "EncryptionModule";
            this.toolTip.SetToolTip(this.EncryptionModule, resources.GetString("EncryptionModule.ToolTip"));
            this.EncryptionModule.SelectedIndexChanged += new System.EventHandler(this.EncryptionModule_SelectedIndexChanged);
            // 
            // GeneratePasswordButton
            // 
            this.GeneratePasswordButton.Image = global::Duplicati.GUI.Properties.Resources.Wizard;
            resources.ApplyResources(this.GeneratePasswordButton, "GeneratePasswordButton");
            this.GeneratePasswordButton.Name = "GeneratePasswordButton";
            this.toolTip.SetToolTip(this.GeneratePasswordButton, resources.GetString("GeneratePasswordButton.ToolTip"));
            this.GeneratePasswordButton.UseVisualStyleBackColor = true;
            this.GeneratePasswordButton.Click += new System.EventHandler(this.GeneratePasswordButton_Click);
            // 
            // EncryptionControlContainer
            // 
            resources.ApplyResources(this.EncryptionControlContainer, "EncryptionControlContainer");
            this.EncryptionControlContainer.Name = "EncryptionControlContainer";
            // 
            // Password
            // 
            this.Password.AskToEnterNewPassword = false;
            this.Password.IsPasswordVisible = false;
            resources.ApplyResources(this.Password, "Password");
            this.Password.MaximumSize = new System.Drawing.Size(5000, 20);
            this.Password.MinimumSize = new System.Drawing.Size(150, 20);
            this.Password.Name = "Password";
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // PasswordSettings
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Password);
            this.Controls.Add(this.EncryptionControlContainer);
            this.Controls.Add(this.GeneratePasswordButton);
            this.Controls.Add(this.EncryptionModule);
            this.Controls.Add(this.EncryptionModuleLabel);
            this.Controls.Add(this.UseSettingsAsDefault);
            this.Controls.Add(this.PasswordHelptext);
            this.Controls.Add(this.EnablePassword);
            this.Name = "PasswordSettings";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox EnablePassword;
        private System.Windows.Forms.Label PasswordHelptext;
        private System.Windows.Forms.CheckBox UseSettingsAsDefault;
        private System.Windows.Forms.Label EncryptionModuleLabel;
        private System.Windows.Forms.ComboBox EncryptionModule;
        private System.Windows.Forms.Button GeneratePasswordButton;
        private System.Windows.Forms.Panel EncryptionControlContainer;
        private Duplicati.Winforms.Controls.PasswordControl Password;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
