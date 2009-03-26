namespace Duplicati.GUI.Wizard_pages
{
    partial class FirstLaunch
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
            this.CreateNew = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.Restore = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // CreateNew
            // 
            this.CreateNew.AutoSize = true;
            this.CreateNew.Location = new System.Drawing.Point(48, 64);
            this.CreateNew.Name = "CreateNew";
            this.CreateNew.Size = new System.Drawing.Size(124, 17);
            this.CreateNew.TabIndex = 0;
            this.CreateNew.TabStop = true;
            this.CreateNew.Text = "Setup a new backup";
            this.CreateNew.UseVisualStyleBackColor = true;
            this.CreateNew.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.CreateNew.CheckedChanged += new System.EventHandler(this.CreateNew_CheckedChanged);
            // 
            // Restore
            // 
            this.Restore.AutoSize = true;
            this.Restore.Location = new System.Drawing.Point(48, 128);
            this.Restore.Name = "Restore";
            this.Restore.Size = new System.Drawing.Size(272, 17);
            this.Restore.TabIndex = 1;
            this.Restore.TabStop = true;
            this.Restore.Text = "Restore settings from a previous Duplicati installation";
            this.Restore.UseVisualStyleBackColor = true;
            this.Restore.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.Restore.CheckedChanged += new System.EventHandler(this.Restore_CheckedChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(48, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(248, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Select one of the two options below, and click next";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(64, 88);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(367, 13);
            this.label2.TabIndex = 3;
            this.label2.Text = "Select this option, if you have not used Duplicati before, or want to start over";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(64, 152);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(395, 13);
            this.label3.TabIndex = 4;
            this.label3.Text = "Select this option if you have used Duplicati before, and want to restore your se" +
                "tup";
            // 
            // FirstLaunch
            // 
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Restore);
            this.Controls.Add(this.CreateNew);
            this.Name = "FirstLaunch";
            this.Size = new System.Drawing.Size(506, 242);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Wizard.DoubleClickRadioButton CreateNew;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton Restore;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;

    }
}
