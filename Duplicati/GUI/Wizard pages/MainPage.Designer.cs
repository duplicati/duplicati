namespace Duplicati.GUI.Wizard_pages
{
    partial class MainPage
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainPage));
            this.label1 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.Backup = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.Remove = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.Restore = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.Edit = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.CreateNew = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.donate_panel = new System.Windows.Forms.Panel();
            this.donate_button = new System.Windows.Forms.PictureBox();
            this.donate_link = new System.Windows.Forms.LinkLabel();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.panel1.SuspendLayout();
            this.donate_panel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.donate_button)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.Backup);
            this.panel1.Controls.Add(this.Remove);
            this.panel1.Controls.Add(this.Restore);
            this.panel1.Controls.Add(this.Edit);
            this.panel1.Controls.Add(this.CreateNew);
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.Name = "panel1";
            // 
            // Backup
            // 
            resources.ApplyResources(this.Backup, "Backup");
            this.Backup.Name = "Backup";
            this.Backup.TabStop = true;
            this.Backup.UseVisualStyleBackColor = true;
            this.Backup.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.Backup.CheckedChanged += new System.EventHandler(this.Radio_CheckedChanged);
            // 
            // Remove
            // 
            resources.ApplyResources(this.Remove, "Remove");
            this.Remove.Name = "Remove";
            this.Remove.TabStop = true;
            this.Remove.UseVisualStyleBackColor = true;
            this.Remove.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.Remove.CheckedChanged += new System.EventHandler(this.Radio_CheckedChanged);
            // 
            // Restore
            // 
            resources.ApplyResources(this.Restore, "Restore");
            this.Restore.Name = "Restore";
            this.Restore.TabStop = true;
            this.Restore.UseVisualStyleBackColor = true;
            this.Restore.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.Restore.CheckedChanged += new System.EventHandler(this.Radio_CheckedChanged);
            // 
            // Edit
            // 
            resources.ApplyResources(this.Edit, "Edit");
            this.Edit.Name = "Edit";
            this.Edit.TabStop = true;
            this.Edit.UseVisualStyleBackColor = true;
            this.Edit.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.Edit.CheckedChanged += new System.EventHandler(this.Radio_CheckedChanged);
            // 
            // CreateNew
            // 
            resources.ApplyResources(this.CreateNew, "CreateNew");
            this.CreateNew.Name = "CreateNew";
            this.CreateNew.TabStop = true;
            this.CreateNew.UseVisualStyleBackColor = true;
            this.CreateNew.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.CreateNew.CheckedChanged += new System.EventHandler(this.Radio_CheckedChanged);
            // 
            // donate_panel
            // 
            this.donate_panel.Controls.Add(this.donate_link);
            this.donate_panel.Controls.Add(this.donate_button);
            resources.ApplyResources(this.donate_panel, "donate_panel");
            this.donate_panel.Name = "donate_panel";
            // 
            // donate_button
            // 
            this.donate_button.Cursor = System.Windows.Forms.Cursors.Hand;
            this.donate_button.Image = global::Duplicati.GUI.Properties.Resources.donate_button;
            resources.ApplyResources(this.donate_button, "donate_button");
            this.donate_button.Name = "donate_button";
            this.donate_button.TabStop = false;
            this.toolTip.SetToolTip(this.donate_button, resources.GetString("donate_button.ToolTip"));
            this.donate_button.Click += new System.EventHandler(this.donate_clicked);
            // 
            // donate_link
            // 
            resources.ApplyResources(this.donate_link, "donate_link");
            this.donate_link.Name = "donate_link";
            this.donate_link.TabStop = true;
            this.toolTip.SetToolTip(this.donate_link, resources.GetString("donate_link.ToolTip"));
            this.donate_link.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.donate_link_LinkClicked);
            // 
            // MainPage
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.donate_panel);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.label1);
            this.Name = "MainPage";
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.donate_panel.ResumeLayout(false);
            this.donate_panel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.donate_button)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton Restore;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton Edit;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton CreateNew;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton Backup;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton Remove;
        private System.Windows.Forms.Panel donate_panel;
        private System.Windows.Forms.PictureBox donate_button;
        private System.Windows.Forms.LinkLabel donate_link;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
