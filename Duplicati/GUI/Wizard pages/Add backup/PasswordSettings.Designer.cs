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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PasswordSettings));
            this.EnablePassword = new System.Windows.Forms.CheckBox();
            this.Password = new System.Windows.Forms.TextBox();
            this.PasswordHelptext = new System.Windows.Forms.Label();
            this.UseSettingsAsDefault = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.EncryptionModule = new System.Windows.Forms.ComboBox();
            this.GeneratePasswordButton = new System.Windows.Forms.Button();
            this.EncryptionControlContainer = new System.Windows.Forms.Panel();
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
            // Password
            // 
            resources.ApplyResources(this.Password, "Password");
            this.Password.Name = "Password";
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
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
            this.UseSettingsAsDefault.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // EncryptionModule
            // 
            this.EncryptionModule.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.EncryptionModule.FormattingEnabled = true;
            resources.ApplyResources(this.EncryptionModule, "EncryptionModule");
            this.EncryptionModule.Name = "EncryptionModule";
            this.EncryptionModule.SelectedIndexChanged += new System.EventHandler(this.EncryptionModule_SelectedIndexChanged);
            // 
            // GeneratePasswordButton
            // 
            resources.ApplyResources(this.GeneratePasswordButton, "GeneratePasswordButton");
            this.GeneratePasswordButton.Name = "GeneratePasswordButton";
            this.GeneratePasswordButton.UseVisualStyleBackColor = true;
            // 
            // EncryptionControlContainer
            // 
            resources.ApplyResources(this.EncryptionControlContainer, "EncryptionControlContainer");
            this.EncryptionControlContainer.Name = "EncryptionControlContainer";
            // 
            // PasswordSettings
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.EncryptionControlContainer);
            this.Controls.Add(this.GeneratePasswordButton);
            this.Controls.Add(this.EncryptionModule);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.UseSettingsAsDefault);
            this.Controls.Add(this.PasswordHelptext);
            this.Controls.Add(this.Password);
            this.Controls.Add(this.EnablePassword);
            this.Name = "PasswordSettings";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox EnablePassword;
        private System.Windows.Forms.TextBox Password;
        private System.Windows.Forms.Label PasswordHelptext;
        private System.Windows.Forms.CheckBox UseSettingsAsDefault;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox EncryptionModule;
        private System.Windows.Forms.Button GeneratePasswordButton;
        private System.Windows.Forms.Panel EncryptionControlContainer;
    }
}
