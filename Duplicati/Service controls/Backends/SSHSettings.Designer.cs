#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
namespace Duplicati.Service_controls
{
    partial class SSHSettings
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
            this.Username = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.Password = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.Passwordless = new System.Windows.Forms.CheckBox();
            this.Host = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.Folder = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.PasswordPanel = new System.Windows.Forms.Panel();
            this.PasswordPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // Username
            // 
            this.Username.Location = new System.Drawing.Point(104, 0);
            this.Username.Name = "Username";
            this.Username.Size = new System.Drawing.Size(152, 20);
            this.Username.TabIndex = 12;
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(0, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(55, 13);
            this.label2.TabIndex = 11;
            this.label2.Text = "Username";
            // 
            // Password
            // 
            this.Password.Location = new System.Drawing.Point(104, 24);
            this.Password.Name = "Password";
            this.Password.Size = new System.Drawing.Size(152, 20);
            this.Password.TabIndex = 14;
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(0, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(53, 13);
            this.label1.TabIndex = 13;
            this.label1.Text = "Password";
            // 
            // Passwordless
            // 
            this.Passwordless.AutoSize = true;
            this.Passwordless.Location = new System.Drawing.Point(8, 0);
            this.Passwordless.Name = "Passwordless";
            this.Passwordless.Size = new System.Drawing.Size(193, 17);
            this.Passwordless.TabIndex = 15;
            this.Passwordless.Text = "Use pageant for passwordless login";
            this.Passwordless.UseVisualStyleBackColor = true;
            this.Passwordless.CheckedChanged += new System.EventHandler(this.Passwordless_CheckedChanged);
            // 
            // Host
            // 
            this.Host.Location = new System.Drawing.Point(111, 81);
            this.Host.Name = "Host";
            this.Host.Size = new System.Drawing.Size(152, 20);
            this.Host.TabIndex = 17;
            this.Host.TextChanged += new System.EventHandler(this.Host_TextChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(7, 81);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(29, 13);
            this.label3.TabIndex = 16;
            this.label3.Text = "Host";
            // 
            // Folder
            // 
            this.Folder.Location = new System.Drawing.Point(112, 104);
            this.Folder.Name = "Folder";
            this.Folder.Size = new System.Drawing.Size(152, 20);
            this.Folder.TabIndex = 19;
            this.Folder.TextChanged += new System.EventHandler(this.Folder_TextChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(8, 104);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(73, 13);
            this.label4.TabIndex = 18;
            this.label4.Text = "Remote folder";
            // 
            // PasswordPanel
            // 
            this.PasswordPanel.Controls.Add(this.Password);
            this.PasswordPanel.Controls.Add(this.label1);
            this.PasswordPanel.Controls.Add(this.Username);
            this.PasswordPanel.Controls.Add(this.label2);
            this.PasswordPanel.Location = new System.Drawing.Point(8, 24);
            this.PasswordPanel.Name = "PasswordPanel";
            this.PasswordPanel.Size = new System.Drawing.Size(256, 48);
            this.PasswordPanel.TabIndex = 20;
            // 
            // SSHSettings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.PasswordPanel);
            this.Controls.Add(this.Folder);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.Host);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.Passwordless);
            this.Name = "SSHSettings";
            this.Size = new System.Drawing.Size(271, 132);
            this.PasswordPanel.ResumeLayout(false);
            this.PasswordPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox Username;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox Password;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox Passwordless;
        private System.Windows.Forms.TextBox Host;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox Folder;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Panel PasswordPanel;
    }
}
