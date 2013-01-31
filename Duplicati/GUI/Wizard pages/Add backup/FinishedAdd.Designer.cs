namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class FinishedAdd
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FinishedAdd));
            this.Summary = new System.Windows.Forms.TextBox();
            this.RunNow = new System.Windows.Forms.CheckBox();
            this.Tabs = new System.Windows.Forms.TabControl();
            this.TabSummary = new System.Windows.Forms.TabPage();
            this.TabCommandLine = new System.Windows.Forms.TabPage();
            this.CommandLine = new System.Windows.Forms.TextBox();
            this.Tabs.SuspendLayout();
            this.TabSummary.SuspendLayout();
            this.TabCommandLine.SuspendLayout();
            this.SuspendLayout();
            // 
            // Summary
            // 
            resources.ApplyResources(this.Summary, "Summary");
            this.Summary.Name = "Summary";
            this.Summary.ReadOnly = true;
            // 
            // RunNow
            // 
            resources.ApplyResources(this.RunNow, "RunNow");
            this.RunNow.Name = "RunNow";
            this.RunNow.UseVisualStyleBackColor = true;
            // 
            // Tabs
            // 
            this.Tabs.Controls.Add(this.TabSummary);
            this.Tabs.Controls.Add(this.TabCommandLine);
            resources.ApplyResources(this.Tabs, "Tabs");
            this.Tabs.Name = "Tabs";
            this.Tabs.SelectedIndex = 0;
            this.Tabs.SelectedIndexChanged += new System.EventHandler(this.Tabs_SelectedIndexChanged);
            // 
            // TabSummary
            // 
            this.TabSummary.Controls.Add(this.Summary);
            resources.ApplyResources(this.TabSummary, "TabSummary");
            this.TabSummary.Name = "TabSummary";
            // 
            // TabCommandLine
            // 
            this.TabCommandLine.Controls.Add(this.CommandLine);
            resources.ApplyResources(this.TabCommandLine, "TabCommandLine");
            this.TabCommandLine.Name = "TabCommandLine";
            // 
            // CommandLine
            // 
            resources.ApplyResources(this.CommandLine, "CommandLine");
            this.CommandLine.Name = "CommandLine";
            this.CommandLine.ReadOnly = true;
            // 
            // FinishedAdd
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Tabs);
            this.Controls.Add(this.RunNow);
            this.Name = "FinishedAdd";
            this.Tabs.ResumeLayout(false);
            this.TabSummary.ResumeLayout(false);
            this.TabSummary.PerformLayout();
            this.TabCommandLine.ResumeLayout(false);
            this.TabCommandLine.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.TextBox Summary;
        public System.Windows.Forms.CheckBox RunNow;
        private System.Windows.Forms.TabControl Tabs;
        private System.Windows.Forms.TabPage TabSummary;
        private System.Windows.Forms.TabPage TabCommandLine;
        public System.Windows.Forms.TextBox CommandLine;
    }
}
