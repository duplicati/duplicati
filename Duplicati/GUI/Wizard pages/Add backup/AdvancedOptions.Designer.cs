namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class AdvancedOptions
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
            this.SelectWhen = new System.Windows.Forms.CheckBox();
            this.SelectIncremental = new System.Windows.Forms.CheckBox();
            this.ThrottleOptions = new System.Windows.Forms.CheckBox();
            this.EditFilters = new System.Windows.Forms.CheckBox();
            this.IncludeDuplicatiSetup = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // SelectWhen
            // 
            this.SelectWhen.AutoSize = true;
            this.SelectWhen.Location = new System.Drawing.Point(32, 32);
            this.SelectWhen.Name = "SelectWhen";
            this.SelectWhen.Size = new System.Drawing.Size(265, 17);
            this.SelectWhen.TabIndex = 0;
            this.SelectWhen.Text = "Select when and how often the backup should run";
            this.SelectWhen.UseVisualStyleBackColor = true;
            // 
            // SelectIncremental
            // 
            this.SelectIncremental.AutoSize = true;
            this.SelectIncremental.Location = new System.Drawing.Point(32, 64);
            this.SelectIncremental.Name = "SelectIncremental";
            this.SelectIncremental.Size = new System.Drawing.Size(439, 17);
            this.SelectIncremental.TabIndex = 1;
            this.SelectIncremental.Text = "Select how often a full backup should be performed, and when old backups are dele" +
                "ted";
            this.SelectIncremental.UseVisualStyleBackColor = true;
            // 
            // ThrottleOptions
            // 
            this.ThrottleOptions.AutoSize = true;
            this.ThrottleOptions.Location = new System.Drawing.Point(32, 96);
            this.ThrottleOptions.Name = "ThrottleOptions";
            this.ThrottleOptions.Size = new System.Drawing.Size(329, 17);
            this.ThrottleOptions.TabIndex = 3;
            this.ThrottleOptions.Text = "Select options that limit machine usage, such as bandwidth limits";
            this.ThrottleOptions.UseVisualStyleBackColor = true;
            // 
            // EditFilters
            // 
            this.EditFilters.AutoSize = true;
            this.EditFilters.Location = new System.Drawing.Point(32, 128);
            this.EditFilters.Name = "EditFilters";
            this.EditFilters.Size = new System.Drawing.Size(316, 17);
            this.EditFilters.TabIndex = 4;
            this.EditFilters.Text = "Modify filters that control what files are included in the backup";
            this.EditFilters.UseVisualStyleBackColor = true;
            // 
            // IncludeDuplicatiSetup
            // 
            this.IncludeDuplicatiSetup.AutoSize = true;
            this.IncludeDuplicatiSetup.Checked = true;
            this.IncludeDuplicatiSetup.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IncludeDuplicatiSetup.Location = new System.Drawing.Point(32, 160);
            this.IncludeDuplicatiSetup.Name = "IncludeDuplicatiSetup";
            this.IncludeDuplicatiSetup.Size = new System.Drawing.Size(256, 17);
            this.IncludeDuplicatiSetup.TabIndex = 5;
            this.IncludeDuplicatiSetup.Text = "Include the current Duplicati setup in the backup";
            this.IncludeDuplicatiSetup.UseVisualStyleBackColor = true;
            // 
            // AdvancedOptions
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.IncludeDuplicatiSetup);
            this.Controls.Add(this.EditFilters);
            this.Controls.Add(this.ThrottleOptions);
            this.Controls.Add(this.SelectIncremental);
            this.Controls.Add(this.SelectWhen);
            this.Name = "AdvancedOptions";
            this.Size = new System.Drawing.Size(506, 242);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox SelectWhen;
        private System.Windows.Forms.CheckBox SelectIncremental;
        private System.Windows.Forms.CheckBox ThrottleOptions;
        private System.Windows.Forms.CheckBox EditFilters;
        private System.Windows.Forms.CheckBox IncludeDuplicatiSetup;
    }
}
