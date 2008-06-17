namespace Duplicati.Wizard_pages.Add_backup
{
    partial class SelectFiles
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
            this.label1 = new System.Windows.Forms.Label();
            this.TargetFolder = new System.Windows.Forms.TextBox();
            this.BrowseFolderButton = new System.Windows.Forms.Button();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.DocumentGroup = new System.Windows.Forms.GroupBox();
            this.label7 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.checkBox6 = new System.Windows.Forms.CheckBox();
            this.checkBox5 = new System.Windows.Forms.CheckBox();
            this.checkBox4 = new System.Windows.Forms.CheckBox();
            this.checkBox3 = new System.Windows.Forms.CheckBox();
            this.checkBox2 = new System.Windows.Forms.CheckBox();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.DocumentsRadio = new System.Windows.Forms.RadioButton();
            this.FolderGroup = new System.Windows.Forms.GroupBox();
            this.label8 = new System.Windows.Forms.Label();
            this.FolderRadio = new System.Windows.Forms.RadioButton();
            this.label9 = new System.Windows.Forms.Label();
            this.DocumentGroup.SuspendLayout();
            this.FolderGroup.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(87, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Folder to backup";
            // 
            // TargetFolder
            // 
            this.TargetFolder.Location = new System.Drawing.Point(152, 24);
            this.TargetFolder.Name = "TargetFolder";
            this.TargetFolder.Size = new System.Drawing.Size(248, 20);
            this.TargetFolder.TabIndex = 1;
            // 
            // BrowseFolderButton
            // 
            this.BrowseFolderButton.Location = new System.Drawing.Point(400, 24);
            this.BrowseFolderButton.Name = "BrowseFolderButton";
            this.BrowseFolderButton.Size = new System.Drawing.Size(24, 20);
            this.BrowseFolderButton.TabIndex = 2;
            this.BrowseFolderButton.Text = "...";
            this.BrowseFolderButton.UseVisualStyleBackColor = true;
            this.BrowseFolderButton.Click += new System.EventHandler(this.BrowseFolderButton_Click);
            // 
            // DocumentGroup
            // 
            this.DocumentGroup.Controls.Add(this.label7);
            this.DocumentGroup.Controls.Add(this.label6);
            this.DocumentGroup.Controls.Add(this.label5);
            this.DocumentGroup.Controls.Add(this.label4);
            this.DocumentGroup.Controls.Add(this.label3);
            this.DocumentGroup.Controls.Add(this.label2);
            this.DocumentGroup.Controls.Add(this.checkBox6);
            this.DocumentGroup.Controls.Add(this.checkBox5);
            this.DocumentGroup.Controls.Add(this.checkBox4);
            this.DocumentGroup.Controls.Add(this.checkBox3);
            this.DocumentGroup.Controls.Add(this.checkBox2);
            this.DocumentGroup.Controls.Add(this.checkBox1);
            this.DocumentGroup.Location = new System.Drawing.Point(24, 16);
            this.DocumentGroup.Name = "DocumentGroup";
            this.DocumentGroup.Size = new System.Drawing.Size(448, 104);
            this.DocumentGroup.TabIndex = 3;
            this.DocumentGroup.TabStop = false;
            // 
            // label7
            // 
            this.label7.Location = new System.Drawing.Point(392, 72);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(32, 16);
            this.label7.TabIndex = 11;
            this.label7.Text = "(...)";
            // 
            // label6
            // 
            this.label6.Location = new System.Drawing.Point(392, 48);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(32, 16);
            this.label6.TabIndex = 10;
            this.label6.Text = "(...)";
            // 
            // label5
            // 
            this.label5.Location = new System.Drawing.Point(392, 24);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(32, 16);
            this.label5.TabIndex = 9;
            this.label5.Text = "(...)";
            // 
            // label4
            // 
            this.label4.Location = new System.Drawing.Point(168, 72);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(32, 16);
            this.label4.TabIndex = 8;
            this.label4.Text = "(...)";
            // 
            // label3
            // 
            this.label3.Location = new System.Drawing.Point(168, 48);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(32, 16);
            this.label3.TabIndex = 7;
            this.label3.Text = "(...)";
            // 
            // label2
            // 
            this.label2.Location = new System.Drawing.Point(168, 24);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(32, 16);
            this.label2.TabIndex = 6;
            this.label2.Text = "(...)";
            // 
            // checkBox6
            // 
            this.checkBox6.AutoSize = true;
            this.checkBox6.Checked = true;
            this.checkBox6.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox6.Location = new System.Drawing.Point(32, 24);
            this.checkBox6.Name = "checkBox6";
            this.checkBox6.Size = new System.Drawing.Size(116, 17);
            this.checkBox6.TabIndex = 5;
            this.checkBox6.Text = "Include documents";
            this.checkBox6.UseVisualStyleBackColor = true;
            // 
            // checkBox5
            // 
            this.checkBox5.AutoSize = true;
            this.checkBox5.Location = new System.Drawing.Point(224, 72);
            this.checkBox5.Name = "checkBox5";
            this.checkBox5.Size = new System.Drawing.Size(154, 17);
            this.checkBox5.TabIndex = 4;
            this.checkBox5.Text = "Include application settings";
            this.checkBox5.UseVisualStyleBackColor = true;
            // 
            // checkBox4
            // 
            this.checkBox4.AutoSize = true;
            this.checkBox4.Checked = true;
            this.checkBox4.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox4.Location = new System.Drawing.Point(224, 48);
            this.checkBox4.Name = "checkBox4";
            this.checkBox4.Size = new System.Drawing.Size(156, 17);
            this.checkBox4.TabIndex = 3;
            this.checkBox4.Text = "Include files on the desktop";
            this.checkBox4.UseVisualStyleBackColor = true;
            // 
            // checkBox3
            // 
            this.checkBox3.AutoSize = true;
            this.checkBox3.Location = new System.Drawing.Point(224, 24);
            this.checkBox3.Name = "checkBox3";
            this.checkBox3.Size = new System.Drawing.Size(113, 17);
            this.checkBox3.TabIndex = 2;
            this.checkBox3.Text = "Include my movies";
            this.checkBox3.UseVisualStyleBackColor = true;
            // 
            // checkBox2
            // 
            this.checkBox2.AutoSize = true;
            this.checkBox2.Location = new System.Drawing.Point(32, 72);
            this.checkBox2.Name = "checkBox2";
            this.checkBox2.Size = new System.Drawing.Size(107, 17);
            this.checkBox2.TabIndex = 1;
            this.checkBox2.Text = "Include my music";
            this.checkBox2.UseVisualStyleBackColor = true;
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Checked = true;
            this.checkBox1.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox1.Location = new System.Drawing.Point(32, 48);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(113, 17);
            this.checkBox1.TabIndex = 0;
            this.checkBox1.Text = "Include my images";
            this.checkBox1.UseVisualStyleBackColor = true;
            // 
            // DocumentsRadio
            // 
            this.DocumentsRadio.AutoSize = true;
            this.DocumentsRadio.Checked = true;
            this.DocumentsRadio.Location = new System.Drawing.Point(40, 16);
            this.DocumentsRadio.Name = "DocumentsRadio";
            this.DocumentsRadio.Size = new System.Drawing.Size(94, 17);
            this.DocumentsRadio.TabIndex = 4;
            this.DocumentsRadio.TabStop = true;
            this.DocumentsRadio.Text = "My documents";
            this.DocumentsRadio.UseVisualStyleBackColor = true;
            this.DocumentsRadio.CheckedChanged += new System.EventHandler(this.TargetType_CheckedChanged);
            // 
            // FolderGroup
            // 
            this.FolderGroup.Controls.Add(this.label8);
            this.FolderGroup.Controls.Add(this.FolderRadio);
            this.FolderGroup.Controls.Add(this.BrowseFolderButton);
            this.FolderGroup.Controls.Add(this.TargetFolder);
            this.FolderGroup.Controls.Add(this.label1);
            this.FolderGroup.Enabled = false;
            this.FolderGroup.Location = new System.Drawing.Point(24, 128);
            this.FolderGroup.Name = "FolderGroup";
            this.FolderGroup.Size = new System.Drawing.Size(448, 56);
            this.FolderGroup.TabIndex = 5;
            this.FolderGroup.TabStop = false;
            // 
            // label8
            // 
            this.label8.Location = new System.Drawing.Point(104, 24);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(32, 16);
            this.label8.TabIndex = 7;
            this.label8.Text = "(...)";
            // 
            // FolderRadio
            // 
            this.FolderRadio.AutoSize = true;
            this.FolderRadio.Location = new System.Drawing.Point(16, 0);
            this.FolderRadio.Name = "FolderRadio";
            this.FolderRadio.Size = new System.Drawing.Size(100, 17);
            this.FolderRadio.TabIndex = 6;
            this.FolderRadio.TabStop = true;
            this.FolderRadio.Text = "A specific folder";
            this.FolderRadio.UseVisualStyleBackColor = true;
            this.FolderRadio.CheckedChanged += new System.EventHandler(this.TargetType_CheckedChanged);
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label9.Location = new System.Drawing.Point(40, 200);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(256, 13);
            this.label9.TabIndex = 6;
            this.label9.Text = "The selected items take up {0} {1} of space";
            // 
            // SelectFiles
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label9);
            this.Controls.Add(this.FolderGroup);
            this.Controls.Add(this.DocumentsRadio);
            this.Controls.Add(this.DocumentGroup);
            this.Name = "SelectFiles";
            this.Size = new System.Drawing.Size(506, 242);
            this.DocumentGroup.ResumeLayout(false);
            this.DocumentGroup.PerformLayout();
            this.FolderGroup.ResumeLayout(false);
            this.FolderGroup.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox TargetFolder;
        private System.Windows.Forms.Button BrowseFolderButton;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
        private System.Windows.Forms.GroupBox DocumentGroup;
        private System.Windows.Forms.RadioButton DocumentsRadio;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox checkBox6;
        private System.Windows.Forms.CheckBox checkBox5;
        private System.Windows.Forms.CheckBox checkBox4;
        private System.Windows.Forms.CheckBox checkBox3;
        private System.Windows.Forms.CheckBox checkBox2;
        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.GroupBox FolderGroup;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.RadioButton FolderRadio;
        private System.Windows.Forms.Label label9;
    }
}
