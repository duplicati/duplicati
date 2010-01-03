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
            this.ThreadPriority = new System.Windows.Forms.ComboBox();
            this.ThreadPriorityEnabled = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // ThreadPriority
            // 
            this.ThreadPriority.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ThreadPriority.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ThreadPriority.Enabled = false;
            this.ThreadPriority.FormattingEnabled = true;
            this.ThreadPriority.Items.AddRange(new object[] {
            "High (Not recommended)",
            "Above normal",
            "Normal",
            "Below normal",
            "Low"});
            this.ThreadPriority.Location = new System.Drawing.Point(152, 0);
            this.ThreadPriority.Name = "ThreadPriority";
            this.ThreadPriority.Size = new System.Drawing.Size(168, 21);
            this.ThreadPriority.TabIndex = 20;
            this.ThreadPriority.SelectedIndexChanged += new System.EventHandler(this.ThreadPriority_SelectedIndexChanged);
            // 
            // ThreadPriorityEnabled
            // 
            this.ThreadPriorityEnabled.AutoSize = true;
            this.ThreadPriorityEnabled.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.ThreadPriorityEnabled.Location = new System.Drawing.Point(0, 0);
            this.ThreadPriorityEnabled.Name = "ThreadPriorityEnabled";
            this.ThreadPriorityEnabled.Size = new System.Drawing.Size(108, 17);
            this.ThreadPriorityEnabled.TabIndex = 19;
            this.ThreadPriorityEnabled.Text = "Set thread priority";
            this.ThreadPriorityEnabled.UseVisualStyleBackColor = true;
            this.ThreadPriorityEnabled.CheckedChanged += new System.EventHandler(this.ThreadPriorityEnabled_CheckedChanged);
            // 
            // ThreadPriorityPicker
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ThreadPriority);
            this.Controls.Add(this.ThreadPriorityEnabled);
            this.Name = "ThreadPriorityPicker";
            this.Size = new System.Drawing.Size(322, 23);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox ThreadPriority;
        private System.Windows.Forms.CheckBox ThreadPriorityEnabled;
    }
}
