namespace Duplicati.Winforms.Controls
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PasswordControl));
            this.TextBox = new System.Windows.Forms.TextBox();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.ShowPassword = new System.Windows.Forms.CheckBox();
            this.Seperator = new System.Windows.Forms.Panel();
            this.SuspendLayout();
            // 
            // TextBox
            // 
            resources.ApplyResources(this.TextBox, "TextBox");
            this.TextBox.Name = "TextBox";
            this.TextBox.UseSystemPasswordChar = true;
            this.TextBox.TextChanged += new System.EventHandler(this.TextBox_TextChanged);
            this.TextBox.Enter += new System.EventHandler(this.TextBox_Enter);
            this.TextBox.Leave += new System.EventHandler(this.TextBox_Leave);
            // 
            // ShowPassword
            // 
            resources.ApplyResources(this.ShowPassword, "ShowPassword");
            this.ShowPassword.Image = global::Duplicati.Winforms.Controls.Properties.Resources.ldots;
            this.ShowPassword.Name = "ShowPassword";
            this.toolTip.SetToolTip(this.ShowPassword, resources.GetString("ShowPassword.ToolTip"));
            this.ShowPassword.UseVisualStyleBackColor = true;
            this.ShowPassword.CheckedChanged += new System.EventHandler(this.ShowPassword_CheckedChanged);
            this.ShowPassword.Click += new System.EventHandler(this.ShowPassword_Click);
            // 
            // Seperator
            // 
            resources.ApplyResources(this.Seperator, "Seperator");
            this.Seperator.Name = "Seperator";
            // 
            // PasswordControl
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.TextBox);
            this.Controls.Add(this.Seperator);
            this.Controls.Add(this.ShowPassword);
            this.MaximumSize = new System.Drawing.Size(5000, 20);
            this.MinimumSize = new System.Drawing.Size(150, 20);
            this.Name = "PasswordControl";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox TextBox;
        private System.Windows.Forms.CheckBox ShowPassword;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.Panel Seperator;
    }
}
