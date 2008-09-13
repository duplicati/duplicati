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
namespace Duplicati.GUI.Service_controls
{
    partial class S3Settings
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
            this.AccessID = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.AccessKey = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.BucketName = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.EuropeanCheckbox = new System.Windows.Forms.CheckBox();
            this.panel1 = new System.Windows.Forms.Panel();
            this.ServerUrl = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // AccessID
            // 
            this.AccessID.Location = new System.Drawing.Point(112, 0);
            this.AccessID.Name = "AccessID";
            this.AccessID.Size = new System.Drawing.Size(152, 20);
            this.AccessID.TabIndex = 14;
            this.AccessID.TextChanged += new System.EventHandler(this.AccessID_TextChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(56, 13);
            this.label2.TabIndex = 13;
            this.label2.Text = "Access ID";
            // 
            // AccessKey
            // 
            this.AccessKey.Location = new System.Drawing.Point(112, 24);
            this.AccessKey.Name = "AccessKey";
            this.AccessKey.Size = new System.Drawing.Size(152, 20);
            this.AccessKey.TabIndex = 16;
            this.AccessKey.TextChanged += new System.EventHandler(this.AccessKey_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(62, 13);
            this.label1.TabIndex = 15;
            this.label1.Text = "Access key";
            // 
            // BucketName
            // 
            this.BucketName.Location = new System.Drawing.Point(112, 48);
            this.BucketName.Name = "BucketName";
            this.BucketName.Size = new System.Drawing.Size(152, 20);
            this.BucketName.TabIndex = 18;
            this.BucketName.TextChanged += new System.EventHandler(this.BucketName_TextChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(8, 48);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(70, 13);
            this.label3.TabIndex = 17;
            this.label3.Text = "Bucket name";
            // 
            // EuropeanCheckbox
            // 
            this.EuropeanCheckbox.AutoSize = true;
            this.EuropeanCheckbox.Location = new System.Drawing.Point(8, 72);
            this.EuropeanCheckbox.Name = "EuropeanCheckbox";
            this.EuropeanCheckbox.Size = new System.Drawing.Size(125, 17);
            this.EuropeanCheckbox.TabIndex = 19;
            this.EuropeanCheckbox.Text = "Use european server";
            this.EuropeanCheckbox.UseVisualStyleBackColor = true;
            this.EuropeanCheckbox.CheckedChanged += new System.EventHandler(this.EuropeanCheckbox_CheckedChanged);
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.ServerUrl);
            this.panel1.Controls.Add(this.label4);
            this.panel1.Location = new System.Drawing.Point(8, 96);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(256, 24);
            this.panel1.TabIndex = 20;
            // 
            // ServerUrl
            // 
            this.ServerUrl.Location = new System.Drawing.Point(104, 0);
            this.ServerUrl.Name = "ServerUrl";
            this.ServerUrl.Size = new System.Drawing.Size(152, 20);
            this.ServerUrl.TabIndex = 22;
            this.ServerUrl.TextChanged += new System.EventHandler(this.ServerUrl_TextChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(0, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(52, 13);
            this.label4.TabIndex = 21;
            this.label4.Text = "Server url";
            // 
            // button1
            // 
            this.button1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.button1.Location = new System.Drawing.Point(160, 128);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(104, 24);
            this.button1.TabIndex = 21;
            this.button1.Text = "Advanced <<<";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // S3Settings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.button1);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.EuropeanCheckbox);
            this.Controls.Add(this.BucketName);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.AccessKey);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.AccessID);
            this.Controls.Add(this.label2);
            this.Name = "S3Settings";
            this.Size = new System.Drawing.Size(269, 160);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox AccessID;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox AccessKey;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox BucketName;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.CheckBox EuropeanCheckbox;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.TextBox ServerUrl;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button button1;
    }
}
