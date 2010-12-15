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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AdvancedOptions));
            this.SelectWhen = new System.Windows.Forms.CheckBox();
            this.SelectCleanup = new System.Windows.Forms.CheckBox();
            this.LimitOptions = new System.Windows.Forms.CheckBox();
            this.EditFilters = new System.Windows.Forms.CheckBox();
            this.IncludeDuplicatiSetup = new System.Windows.Forms.CheckBox();
            this.EditOverrides = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // SelectWhen
            // 
            resources.ApplyResources(this.SelectWhen, "SelectWhen");
            this.SelectWhen.Name = "SelectWhen";
            this.SelectWhen.UseVisualStyleBackColor = true;
            // 
            // SelectCleanup
            // 
            resources.ApplyResources(this.SelectCleanup, "SelectCleanup");
            this.SelectCleanup.Name = "SelectCleanup";
            this.SelectCleanup.UseVisualStyleBackColor = true;
            // 
            // LimitOptions
            // 
            resources.ApplyResources(this.LimitOptions, "LimitOptions");
            this.LimitOptions.Name = "LimitOptions";
            this.LimitOptions.UseVisualStyleBackColor = true;
            // 
            // EditFilters
            // 
            resources.ApplyResources(this.EditFilters, "EditFilters");
            this.EditFilters.Name = "EditFilters";
            this.EditFilters.UseVisualStyleBackColor = true;
            // 
            // IncludeDuplicatiSetup
            // 
            resources.ApplyResources(this.IncludeDuplicatiSetup, "IncludeDuplicatiSetup");
            this.IncludeDuplicatiSetup.Checked = true;
            this.IncludeDuplicatiSetup.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IncludeDuplicatiSetup.Name = "IncludeDuplicatiSetup";
            this.IncludeDuplicatiSetup.UseVisualStyleBackColor = true;
            // 
            // EditOverrides
            // 
            resources.ApplyResources(this.EditOverrides, "EditOverrides");
            this.EditOverrides.Name = "EditOverrides";
            this.EditOverrides.UseVisualStyleBackColor = true;
            // 
            // AdvancedOptions
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.EditOverrides);
            this.Controls.Add(this.IncludeDuplicatiSetup);
            this.Controls.Add(this.EditFilters);
            this.Controls.Add(this.LimitOptions);
            this.Controls.Add(this.SelectCleanup);
            this.Controls.Add(this.SelectWhen);
            this.Name = "AdvancedOptions";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox SelectWhen;
        private System.Windows.Forms.CheckBox SelectCleanup;
        private System.Windows.Forms.CheckBox LimitOptions;
        private System.Windows.Forms.CheckBox EditFilters;
        private System.Windows.Forms.CheckBox IncludeDuplicatiSetup;
        private System.Windows.Forms.CheckBox EditOverrides;
    }
}
