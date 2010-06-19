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
            this.UseEUBuckets = new System.Windows.Forms.CheckBox();
            this.AllowCredentialStorage = new System.Windows.Forms.CheckBox();
            this.CredentialGroup.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // CredentialGroup
            // 
            resources.ApplyResources(this.CredentialGroup, "CredentialGroup");
            this.CredentialGroup.Controls.Add(this.RemoveSelectedButton);
            this.CredentialGroup.Controls.Add(this.RemoveAllButton);
            this.CredentialGroup.Controls.Add(this.CredentialList);
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
            resources.ApplyResources(this.CredentialList, "CredentialList");
            this.CredentialList.FormattingEnabled = true;
            this.CredentialList.Name = "CredentialList";
            this.CredentialList.SelectedIndexChanged += new System.EventHandler(this.CredentialList_SelectedIndexChanged);
            // 
            // groupBox2
            // 
            resources.ApplyResources(this.groupBox2, "groupBox2");
            this.groupBox2.Controls.Add(this.UseEUBuckets);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.TabStop = false;
            // 
            // UseEUBuckets
            // 
            resources.ApplyResources(this.UseEUBuckets, "UseEUBuckets");
            this.UseEUBuckets.Name = "UseEUBuckets";
            this.UseEUBuckets.UseVisualStyleBackColor = true;
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
            this.Controls.Add(this.AllowCredentialStorage);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.CredentialGroup);
            this.MinimumSize = new System.Drawing.Size(265, 232);
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
        private System.Windows.Forms.CheckBox UseEUBuckets;
        private System.Windows.Forms.CheckBox AllowCredentialStorage;
    }
}
