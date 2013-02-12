namespace Duplicati.GUI.HelperControls
{
    partial class ThreadPriorityPicker
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ThreadPriorityPicker));
            this.ThreadPriority = new System.Windows.Forms.ComboBox();
            this.ThreadPriorityEnabled = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // ThreadPriority
            // 
            resources.ApplyResources(this.ThreadPriority, "ThreadPriority");
            this.ThreadPriority.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ThreadPriority.FormattingEnabled = true;
            this.ThreadPriority.Items.AddRange(new object[] {
            resources.GetString("ThreadPriority.Items"),
            resources.GetString("ThreadPriority.Items1"),
            resources.GetString("ThreadPriority.Items2"),
            resources.GetString("ThreadPriority.Items3"),
            resources.GetString("ThreadPriority.Items4")});
            this.ThreadPriority.Name = "ThreadPriority";
            this.ThreadPriority.SelectedIndexChanged += new System.EventHandler(this.ThreadPriority_SelectedIndexChanged);
            // 
            // ThreadPriorityEnabled
            // 
            resources.ApplyResources(this.ThreadPriorityEnabled, "ThreadPriorityEnabled");
            this.ThreadPriorityEnabled.Name = "ThreadPriorityEnabled";
            this.ThreadPriorityEnabled.UseVisualStyleBackColor = true;
            this.ThreadPriorityEnabled.CheckedChanged += new System.EventHandler(this.ThreadPriorityEnabled_CheckedChanged);
            // 
            // ThreadPriorityPicker
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ThreadPriority);
            this.Controls.Add(this.ThreadPriorityEnabled);
            this.Name = "ThreadPriorityPicker";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox ThreadPriority;
        private System.Windows.Forms.CheckBox ThreadPriorityEnabled;
    }
}
