namespace Duplicati.Scheduler
{
    partial class EnterPassDialog
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
            this.button1 = new System.Windows.Forms.Button();
            this.secureTextBox1 = new Utility.SecureTextBox();
            this.SuspendLayout();
            // 
            // button1
            // 
            this.button1.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button1.Dock = System.Windows.Forms.DockStyle.Top;
            this.button1.FlatAppearance.BorderSize = 0;
            this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button1.Location = new System.Drawing.Point(0, 0);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(224, 31);
            this.button1.TabIndex = 2;
            this.button1.Text = "Enter current password:";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // secureTextBox1
            // 
            this.secureTextBox1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.secureTextBox1.Location = new System.Drawing.Point(0, 32);
            this.secureTextBox1.Name = "secureTextBox1";
            this.secureTextBox1.ShortcutsEnabled = false;
            this.secureTextBox1.Size = new System.Drawing.Size(224, 27);
            this.secureTextBox1.TabIndex = 1;
            this.secureTextBox1.UseSystemPasswordChar = true;
            // 
            // EnterPassDialog
            // 
            this.AcceptButton = this.button1;
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.button1;
            this.ClientSize = new System.Drawing.Size(224, 59);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.secureTextBox1);
            this.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "EnterPassDialog";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Utility.SecureTextBox secureTextBox1;
        private System.Windows.Forms.Button button1;
    }
}