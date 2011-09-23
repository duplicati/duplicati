namespace Duplicati.Scheduler
{
    partial class PasswordControl
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PasswordControl));
            this.PaGroupBox = new System.Windows.Forms.GroupBox();
            this.SecureGroupBox = new System.Windows.Forms.GroupBox();
            this.secureTextBox1 = new Utility.SecureTextBox();
            this.ChangeButton = new System.Windows.Forms.Button();
            this.PasswordHelptext = new System.Windows.Forms.Label();
            this.EncryptionModuleLabel = new System.Windows.Forms.Label();
            this.EncryptionModuleDropDown = new System.Windows.Forms.ComboBox();
            this.PaGroupBox.SuspendLayout();
            this.SecureGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // PaGroupBox
            // 
            this.PaGroupBox.Controls.Add(this.SecureGroupBox);
            this.PaGroupBox.Controls.Add(this.EncryptionModuleLabel);
            this.PaGroupBox.Controls.Add(this.EncryptionModuleDropDown);
            this.PaGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PaGroupBox.Location = new System.Drawing.Point(0, 0);
            this.PaGroupBox.Name = "PaGroupBox";
            this.PaGroupBox.Size = new System.Drawing.Size(517, 173);
            this.PaGroupBox.TabIndex = 26;
            this.PaGroupBox.TabStop = false;
            this.PaGroupBox.Text = "Password";
            // 
            // SecureGroupBox
            // 
            this.SecureGroupBox.Controls.Add(this.secureTextBox1);
            this.SecureGroupBox.Controls.Add(this.ChangeButton);
            this.SecureGroupBox.Controls.Add(this.PasswordHelptext);
            this.SecureGroupBox.Location = new System.Drawing.Point(6, 16);
            this.SecureGroupBox.Name = "SecureGroupBox";
            this.SecureGroupBox.Size = new System.Drawing.Size(503, 110);
            this.SecureGroupBox.TabIndex = 28;
            this.SecureGroupBox.TabStop = false;
            // 
            // secureTextBox1
            // 
            this.secureTextBox1.Location = new System.Drawing.Point(13, 26);
            this.secureTextBox1.Name = "secureTextBox1";
            this.secureTextBox1.ReadOnly = true;
            this.secureTextBox1.ShortcutsEnabled = false;
            this.secureTextBox1.Size = new System.Drawing.Size(387, 27);
            this.secureTextBox1.TabIndex = 26;
            this.secureTextBox1.Text = "*********************************************************************************" +
                "*************************************************************";
            this.secureTextBox1.UseSystemPasswordChar = true;
            // 
            // ChangeButton
            // 
            this.ChangeButton.Location = new System.Drawing.Point(406, 25);
            this.ChangeButton.Name = "ChangeButton";
            this.ChangeButton.Size = new System.Drawing.Size(84, 28);
            this.ChangeButton.TabIndex = 27;
            this.ChangeButton.Text = "Change";
            this.ChangeButton.UseVisualStyleBackColor = true;
            this.ChangeButton.Click += new System.EventHandler(this.ChangeButtonClick);
            // 
            // PasswordHelptext
            // 
            this.PasswordHelptext.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic);
            this.PasswordHelptext.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.PasswordHelptext.Location = new System.Drawing.Point(10, 56);
            this.PasswordHelptext.Name = "PasswordHelptext";
            this.PasswordHelptext.Size = new System.Drawing.Size(480, 48);
            this.PasswordHelptext.TabIndex = 16;
            this.PasswordHelptext.Text = resources.GetString("PasswordHelptext.Text");
            // 
            // EncryptionModuleLabel
            // 
            this.EncryptionModuleLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.EncryptionModuleLabel.AutoSize = true;
            this.EncryptionModuleLabel.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.EncryptionModuleLabel.Location = new System.Drawing.Point(16, 143);
            this.EncryptionModuleLabel.Name = "EncryptionModuleLabel";
            this.EncryptionModuleLabel.Size = new System.Drawing.Size(143, 19);
            this.EncryptionModuleLabel.TabIndex = 18;
            this.EncryptionModuleLabel.Text = "Encryption method";
            // 
            // EncryptionModuleDropDown
            // 
            this.EncryptionModuleDropDown.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.EncryptionModuleDropDown.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.EncryptionModuleDropDown.FormattingEnabled = true;
            this.EncryptionModuleDropDown.Location = new System.Drawing.Point(165, 140);
            this.EncryptionModuleDropDown.Name = "EncryptionModuleDropDown";
            this.EncryptionModuleDropDown.Size = new System.Drawing.Size(331, 27);
            this.EncryptionModuleDropDown.TabIndex = 19;
            // 
            // PasswordControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Gainsboro;
            this.Controls.Add(this.PaGroupBox);
            this.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "PasswordControl";
            this.Size = new System.Drawing.Size(517, 173);
            this.PaGroupBox.ResumeLayout(false);
            this.PaGroupBox.PerformLayout();
            this.SecureGroupBox.ResumeLayout(false);
            this.SecureGroupBox.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox PaGroupBox;
        private System.Windows.Forms.Label PasswordHelptext;
        private System.Windows.Forms.Label EncryptionModuleLabel;
        private System.Windows.Forms.ComboBox EncryptionModuleDropDown;
        private Utility.SecureTextBox secureTextBox1;
        private System.Windows.Forms.Button ChangeButton;
        private System.Windows.Forms.GroupBox SecureGroupBox;
    }
}
