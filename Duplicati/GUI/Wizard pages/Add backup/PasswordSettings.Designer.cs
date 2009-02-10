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
            this.UseGPG = new System.Windows.Forms.CheckBox();
            this.PasswordGeneratorSettings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PassphraseLength)).BeginInit();
            this.SuspendLayout();
            // 
            // EnablePassword
            // 
            this.EnablePassword.AutoSize = true;
            this.EnablePassword.Checked = true;
            this.EnablePassword.CheckState = System.Windows.Forms.CheckState.Checked;
            this.EnablePassword.Location = new System.Drawing.Point(24, 8);
            this.EnablePassword.Name = "EnablePassword";
            this.EnablePassword.Size = new System.Drawing.Size(211, 17);
            this.EnablePassword.TabIndex = 0;
            this.EnablePassword.Text = "Protect the backups with this password";
            this.EnablePassword.UseVisualStyleBackColor = true;
            this.EnablePassword.CheckedChanged += new System.EventHandler(this.EnablePassword_CheckedChanged);
            // 
            // Password
            // 
            this.Password.Location = new System.Drawing.Point(240, 8);
            this.Password.Name = "Password";
            this.Password.Size = new System.Drawing.Size(224, 20);
            this.Password.TabIndex = 1;
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // PasswordHelptext
            // 
            this.PasswordHelptext.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.PasswordHelptext.Location = new System.Drawing.Point(24, 32);
            this.PasswordHelptext.Name = "PasswordHelptext";
            this.PasswordHelptext.Size = new System.Drawing.Size(448, 48);
            this.PasswordHelptext.TabIndex = 3;
            this.PasswordHelptext.Text = resources.GetString("PasswordHelptext.Text");
            // 
            // PasswordGeneratorSettings
            // 
            this.PasswordGeneratorSettings.Controls.Add(this.PasswordCharacterSet);
            this.PasswordGeneratorSettings.Controls.Add(this.label3);
            this.PasswordGeneratorSettings.Controls.Add(this.GeneratePassword);
            this.PasswordGeneratorSettings.Controls.Add(this.PassphraseLength);
            this.PasswordGeneratorSettings.Controls.Add(this.label2);
            this.PasswordGeneratorSettings.Location = new System.Drawing.Point(24, 80);
            this.PasswordGeneratorSettings.Name = "PasswordGeneratorSettings";
            this.PasswordGeneratorSettings.Size = new System.Drawing.Size(448, 56);
            this.PasswordGeneratorSettings.TabIndex = 4;
            this.PasswordGeneratorSettings.TabStop = false;
            this.PasswordGeneratorSettings.Text = "Generate a password";
            // 
            // PasswordCharacterSet
            // 
            this.PasswordCharacterSet.Enabled = false;
            this.PasswordCharacterSet.FormattingEnabled = true;
            this.PasswordCharacterSet.Items.AddRange(new object[] {
            "Uppercase + Lowercase + Number + Punctiation + Various",
            "Uppercase + Lowercase + Number + Punctiation",
            "Uppercase + Lowercase + Numbers",
            "Uppercase only",
            "Lowercase only",
            "Numbers only",
            "Uppercase + Numbers",
            "Lowercase + Numbers"});
            this.PasswordCharacterSet.Location = new System.Drawing.Point(80, 24);
            this.PasswordCharacterSet.Name = "PasswordCharacterSet";
            this.PasswordCharacterSet.Size = new System.Drawing.Size(80, 21);
            this.PasswordCharacterSet.TabIndex = 9;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Enabled = false;
            this.label3.Location = new System.Drawing.Point(8, 24);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(58, 13);
            this.label3.TabIndex = 8;
            this.label3.Text = "Characters";
            // 
            // GeneratePassword
            // 
            this.GeneratePassword.Location = new System.Drawing.Point(312, 24);
            this.GeneratePassword.Name = "GeneratePassword";
            this.GeneratePassword.Size = new System.Drawing.Size(128, 24);
            this.GeneratePassword.TabIndex = 7;
            this.GeneratePassword.Text = "Generate password";
            this.GeneratePassword.UseVisualStyleBackColor = true;
            this.GeneratePassword.Click += new System.EventHandler(this.GeneratePassword_Click);
            // 
            // PassphraseLength
            // 
            this.PassphraseLength.Location = new System.Drawing.Point(232, 24);
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
            this.PassphraseLength.Size = new System.Drawing.Size(72, 20);
            this.PassphraseLength.TabIndex = 6;
            this.PassphraseLength.Value = new decimal(new int[] {
            12,
            0,
            0,
            0});
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(168, 24);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(58, 13);
            this.label2.TabIndex = 5;
            this.label2.Text = "Characters";
            // 
            // UseGPG
            // 
            this.UseGPG.AutoSize = true;
            this.UseGPG.Location = new System.Drawing.Point(24, 176);
            this.UseGPG.Name = "UseGPG";
            this.UseGPG.Size = new System.Drawing.Size(365, 17);
            this.UseGPG.TabIndex = 5;
            this.UseGPG.Text = "Use GNU Privacy Guard for encryption (requires that GnuPG is installed)";
            this.UseGPG.UseVisualStyleBackColor = true;
            this.UseGPG.CheckedChanged += new System.EventHandler(this.UseGPG_CheckedChanged);
            // 
            // PasswordSettings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.UseGPG);
            this.Controls.Add(this.PasswordGeneratorSettings);
            this.Controls.Add(this.PasswordHelptext);
            this.Controls.Add(this.Password);
            this.Controls.Add(this.EnablePassword);
            this.Name = "PasswordSettings";
            this.Size = new System.Drawing.Size(506, 242);
            this.PasswordGeneratorSettings.ResumeLayout(false);
            this.PasswordGeneratorSettings.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PassphraseLength)).EndInit();
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
        private System.Windows.Forms.CheckBox UseGPG;
    }
}
