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
    partial class FileSettings
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
            this.button1 = new System.Windows.Forms.Button();
            this.panel1 = new System.Windows.Forms.Panel();
            this.TimeSeperator = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.BrowseTargetFolder = new System.Windows.Forms.Button();
            this.DestinationFolder = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // button1
            // 
            this.button1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.button1.Location = new System.Drawing.Point(160, 63);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(104, 24);
            this.button1.TabIndex = 2;
            this.button1.Text = "Advanced <<<";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.TimeSeperator);
            this.panel1.Controls.Add(this.label2);
            this.panel1.Location = new System.Drawing.Point(0, 24);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(264, 32);
            this.panel1.TabIndex = 3;
            // 
            // TimeSeperator
            // 
            this.TimeSeperator.Location = new System.Drawing.Point(112, 8);
            this.TimeSeperator.Name = "TimeSeperator";
            this.TimeSeperator.Size = new System.Drawing.Size(24, 20);
            this.TimeSeperator.TabIndex = 2;
            this.TimeSeperator.TextChanged += new System.EventHandler(this.TimeSeperator_TextChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 8);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(77, 13);
            this.label2.TabIndex = 0;
            this.label2.Text = "Time seperator";
            // 
            // BrowseTargetFolder
            // 
            this.BrowseTargetFolder.Location = new System.Drawing.Point(240, 0);
            this.BrowseTargetFolder.Name = "BrowseTargetFolder";
            this.BrowseTargetFolder.Size = new System.Drawing.Size(24, 20);
            this.BrowseTargetFolder.TabIndex = 13;
            this.BrowseTargetFolder.Text = "...";
            this.BrowseTargetFolder.UseVisualStyleBackColor = true;
            this.BrowseTargetFolder.Click += new System.EventHandler(this.BrowseTargetFolder_Click);
            // 
            // DestinationFolder
            // 
            this.DestinationFolder.Location = new System.Drawing.Point(112, 0);
            this.DestinationFolder.Name = "DestinationFolder";
            this.DestinationFolder.Size = new System.Drawing.Size(128, 20);
            this.DestinationFolder.TabIndex = 12;
            this.DestinationFolder.TextChanged += new System.EventHandler(this.DestinationFolder_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(98, 13);
            this.label1.TabIndex = 11;
            this.label1.Text = "Backup destination";
            // 
            // FileSettings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.BrowseTargetFolder);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.DestinationFolder);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label1);
            this.Name = "FileSettings";
            this.Size = new System.Drawing.Size(268, 96);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.TextBox TimeSeperator;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button BrowseTargetFolder;
        private System.Windows.Forms.TextBox DestinationFolder;
        private System.Windows.Forms.Label label1;
    }
}
