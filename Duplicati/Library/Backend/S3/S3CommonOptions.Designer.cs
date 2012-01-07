namespace Duplicati.Library.Backend
{
    partial class S3CommonOptions
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(S3CommonOptions));
            this.CredentialGroup = new System.Windows.Forms.GroupBox();
            this.RemoveSelectedButton = new System.Windows.Forms.Button();
            this.RemoveAllButton = new System.Windows.Forms.Button();
            this.CredentialList = new System.Windows.Forms.ListBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.DefaultServername = new System.Windows.Forms.ComboBox();
            this.label2 = new System.Windows.Forms.Label();
            this.DefaultBucketRegion = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.UseRRS = new System.Windows.Forms.CheckBox();
            this.AllowCredentialStorage = new System.Windows.Forms.CheckBox();
            this.CredentialGroup.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // CredentialGroup
            // 
            this.CredentialGroup.Controls.Add(this.RemoveSelectedButton);
            this.CredentialGroup.Controls.Add(this.RemoveAllButton);
            this.CredentialGroup.Controls.Add(this.CredentialList);
            resources.ApplyResources(this.CredentialGroup, "CredentialGroup");
            this.CredentialGroup.Name = "CredentialGroup";
            this.CredentialGroup.TabStop = false;
            // 
            // RemoveSelectedButton
            // 
            resources.ApplyResources(this.RemoveSelectedButton, "RemoveSelectedButton");
            this.RemoveSelectedButton.Name = "RemoveSelectedButton";
            this.RemoveSelectedButton.UseVisualStyleBackColor = true;
            this.RemoveSelectedButton.Click += new System.EventHandler(this.RemoveSelectedButton_Click);
            // 
            // RemoveAllButton
            // 
            resources.ApplyResources(this.RemoveAllButton, "RemoveAllButton");
            this.RemoveAllButton.Name = "RemoveAllButton";
            this.RemoveAllButton.UseVisualStyleBackColor = true;
            this.RemoveAllButton.Click += new System.EventHandler(this.RemoveAllButton_Click);
            // 
            // CredentialList
            // 
            this.CredentialList.FormattingEnabled = true;
            resources.ApplyResources(this.CredentialList, "CredentialList");
            this.CredentialList.Name = "CredentialList";
            this.CredentialList.SelectedIndexChanged += new System.EventHandler(this.CredentialList_SelectedIndexChanged);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.DefaultServername);
            this.groupBox2.Controls.Add(this.label2);
            this.groupBox2.Controls.Add(this.DefaultBucketRegion);
            this.groupBox2.Controls.Add(this.label1);
            this.groupBox2.Controls.Add(this.UseRRS);
            resources.ApplyResources(this.groupBox2, "groupBox2");
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.TabStop = false;
            // 
            // DefaultServername
            // 
            this.DefaultServername.FormattingEnabled = true;
            resources.ApplyResources(this.DefaultServername, "DefaultServername");
            this.DefaultServername.Name = "DefaultServername";
            this.DefaultServername.SelectedIndexChanged += new System.EventHandler(this.DefaultServername_SelectedIndexChanged);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // DefaultBucketRegion
            // 
            this.DefaultBucketRegion.FormattingEnabled = true;
            resources.ApplyResources(this.DefaultBucketRegion, "DefaultBucketRegion");
            this.DefaultBucketRegion.Name = "DefaultBucketRegion";
            this.DefaultBucketRegion.SelectedIndexChanged += new System.EventHandler(this.DefaultBucketRegion_SelectedIndexChanged);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // UseRRS
            // 
            resources.ApplyResources(this.UseRRS, "UseRRS");
            this.UseRRS.Name = "UseRRS";
            this.UseRRS.UseVisualStyleBackColor = true;
            // 
            // AllowCredentialStorage
            // 
            resources.ApplyResources(this.AllowCredentialStorage, "AllowCredentialStorage");
            this.AllowCredentialStorage.Checked = true;
            this.AllowCredentialStorage.CheckState = System.Windows.Forms.CheckState.Checked;
            this.AllowCredentialStorage.Name = "AllowCredentialStorage";
            this.AllowCredentialStorage.UseVisualStyleBackColor = true;
            this.AllowCredentialStorage.CheckedChanged += new System.EventHandler(this.AllowCredentialStorage_CheckedChanged);
            // 
            // S3CommonOptions
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.Controls.Add(this.AllowCredentialStorage);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.CredentialGroup);
            this.MinimumSize = new System.Drawing.Size(265, 257);
            this.Name = "S3CommonOptions";
            this.CredentialGroup.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox CredentialGroup;
        private System.Windows.Forms.Button RemoveSelectedButton;
        private System.Windows.Forms.Button RemoveAllButton;
        private System.Windows.Forms.ListBox CredentialList;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.CheckBox AllowCredentialStorage;
        private System.Windows.Forms.CheckBox UseRRS;
        private System.Windows.Forms.ComboBox DefaultBucketRegion;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox DefaultServername;
        private System.Windows.Forms.Label label2;
    }
}
