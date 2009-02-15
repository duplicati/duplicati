namespace Duplicati.GUI.Wizard_pages.Restore
{
    partial class FinishedRestore
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
            this.Summary = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.RunInBackground = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // Summary
            // 
            this.Summary.Font = new System.Drawing.Font("Courier New", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Summary.Location = new System.Drawing.Point(25, 40);
            this.Summary.Multiline = true;
            this.Summary.Name = "Summary";
            this.Summary.ReadOnly = true;
            this.Summary.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.Summary.Size = new System.Drawing.Size(456, 144);
            this.Summary.TabIndex = 3;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(25, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(50, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Summary";
            // 
            // RunInBackground
            // 
            this.RunInBackground.AutoSize = true;
            this.RunInBackground.Location = new System.Drawing.Point(24, 200);
            this.RunInBackground.Name = "RunInBackground";
            this.RunInBackground.Size = new System.Drawing.Size(228, 17);
            this.RunInBackground.TabIndex = 4;
            this.RunInBackground.Text = "Run restore operation as a background job";
            this.RunInBackground.UseVisualStyleBackColor = true;
            // 
            // FinishedRestore
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RunInBackground);
            this.Controls.Add(this.Summary);
            this.Controls.Add(this.label1);
            this.Name = "FinishedRestore";
            this.Size = new System.Drawing.Size(506, 242);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.TextBox Summary;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox RunInBackground;
    }
}
