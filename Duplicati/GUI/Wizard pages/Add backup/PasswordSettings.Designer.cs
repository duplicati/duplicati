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
            this.PasswordGeneratorSettings = new System.Windows.Forms.GroupBox();
            this.PasswordCharacterSet = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            this.GeneratePassword = new System.Windows.Forms.Button();
            this.PassphraseLength = new System.Windows.Forms.NumericUpDown();
            this.label2 = new System.Windows.Forms.Label();
            this.UseSettingsAsDefault = new System.Windows.Forms.CheckBox();
            this.EncryptionMethod = new System.Windows.Forms.GroupBox();
            this.UseGPGEncryption = new System.Windows.Forms.RadioButton();
            this.UseAESEncryption = new System.Windows.Forms.RadioButton();
            this.PasswordGeneratorSettings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PassphraseLength)).BeginInit();
            this.EncryptionMethod.SuspendLayout();
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
            // PasswordGeneratorSettings
            // 
            this.PasswordGeneratorSettings.Controls.Add(this.PasswordCharacterSet);
            this.PasswordGeneratorSettings.Controls.Add(this.label3);
            this.PasswordGeneratorSettings.Controls.Add(this.GeneratePassword);
            this.PasswordGeneratorSettings.Controls.Add(this.PassphraseLength);
            this.PasswordGeneratorSettings.Controls.Add(this.label2);
            resources.ApplyResources(this.PasswordGeneratorSettings, "PasswordGeneratorSettings");
            this.PasswordGeneratorSettings.Name = "PasswordGeneratorSettings";
            this.PasswordGeneratorSettings.TabStop = false;
            // 
            // PasswordCharacterSet
            // 
            resources.ApplyResources(this.PasswordCharacterSet, "PasswordCharacterSet");
            this.PasswordCharacterSet.FormattingEnabled = true;
            this.PasswordCharacterSet.Items.AddRange(new object[] {
            resources.GetString("PasswordCharacterSet.Items"),
            resources.GetString("PasswordCharacterSet.Items1"),
            resources.GetString("PasswordCharacterSet.Items2"),
            resources.GetString("PasswordCharacterSet.Items3"),
            resources.GetString("PasswordCharacterSet.Items4"),
            resources.GetString("PasswordCharacterSet.Items5"),
            resources.GetString("PasswordCharacterSet.Items6"),
            resources.GetString("PasswordCharacterSet.Items7")});
            this.PasswordCharacterSet.Name = "PasswordCharacterSet";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // GeneratePassword
            // 
            resources.ApplyResources(this.GeneratePassword, "GeneratePassword");
            this.GeneratePassword.Name = "GeneratePassword";
            this.GeneratePassword.UseVisualStyleBackColor = true;
            this.GeneratePassword.Click += new System.EventHandler(this.GeneratePassword_Click);
            // 
            // PassphraseLength
            // 
            resources.ApplyResources(this.PassphraseLength, "PassphraseLength");
            this.PassphraseLength.Maximum = new decimal(new int[] {
            64,
            0,
            0,
            0});
            this.PassphraseLength.Minimum = new decimal(new int[] {
            8,
            0,
            0,
            0});
            this.PassphraseLength.Name = "PassphraseLength";
            this.PassphraseLength.Value = new decimal(new int[] {
            12,
            0,
            0,
            0});
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // UseSettingsAsDefault
            // 
            resources.ApplyResources(this.UseSettingsAsDefault, "UseSettingsAsDefault");
            this.UseSettingsAsDefault.Name = "UseSettingsAsDefault";
            this.UseSettingsAsDefault.UseVisualStyleBackColor = true;
            // 
            // EncryptionMethod
            // 
            this.EncryptionMethod.Controls.Add(this.UseGPGEncryption);
            this.EncryptionMethod.Controls.Add(this.UseAESEncryption);
            resources.ApplyResources(this.EncryptionMethod, "EncryptionMethod");
            this.EncryptionMethod.Name = "EncryptionMethod";
            this.EncryptionMethod.TabStop = false;
            // 
            // UseGPGEncryption
            // 
            resources.ApplyResources(this.UseGPGEncryption, "UseGPGEncryption");
            this.UseGPGEncryption.Name = "UseGPGEncryption";
            this.UseGPGEncryption.UseVisualStyleBackColor = true;
            this.UseGPGEncryption.CheckedChanged += new System.EventHandler(this.UseGPGEncryption_CheckedChanged);
            // 
            // UseAESEncryption
            // 
            resources.ApplyResources(this.UseAESEncryption, "UseAESEncryption");
            this.UseAESEncryption.Checked = true;
            this.UseAESEncryption.Name = "UseAESEncryption";
            this.UseAESEncryption.TabStop = true;
            this.UseAESEncryption.UseVisualStyleBackColor = true;
            this.UseAESEncryption.CheckedChanged += new System.EventHandler(this.UseAESEncryption_CheckedChanged);
            // 
            // PasswordSettings
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.EncryptionMethod);
            this.Controls.Add(this.UseSettingsAsDefault);
            this.Controls.Add(this.PasswordGeneratorSettings);
            this.Controls.Add(this.PasswordHelptext);
            this.Controls.Add(this.Password);
            this.Controls.Add(this.EnablePassword);
            this.Name = "PasswordSettings";
            this.PasswordGeneratorSettings.ResumeLayout(false);
            this.PasswordGeneratorSettings.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PassphraseLength)).EndInit();
            this.EncryptionMethod.ResumeLayout(false);
            this.EncryptionMethod.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox EnablePassword;
        private System.Windows.Forms.TextBox Password;
        private System.Windows.Forms.Label PasswordHelptext;
        private System.Windows.Forms.GroupBox PasswordGeneratorSettings;
        private System.Windows.Forms.Button GeneratePassword;
        private System.Windows.Forms.NumericUpDown PassphraseLength;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ComboBox PasswordCharacterSet;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.CheckBox UseSettingsAsDefault;
        private System.Windows.Forms.GroupBox EncryptionMethod;
        private System.Windows.Forms.RadioButton UseGPGEncryption;
        private System.Windows.Forms.RadioButton UseAESEncryption;
    }
}
