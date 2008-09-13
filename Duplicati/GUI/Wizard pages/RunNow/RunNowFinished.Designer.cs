namespace Duplicati.Wizard_pages.RunNow
{
    partial class RunNowFinished
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
            this.ForceFull = new System.Windows.Forms.CheckBox();
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
            this.Summary.TabIndex = 5;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(25, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(50, 13);
            this.label1.TabIndex = 4;
            this.label1.Text = "Summary";
            // 
            // ForceFull
            // 
            this.ForceFull.AutoSize = true;
            this.ForceFull.Location = new System.Drawing.Point(24, 200);
            this.ForceFull.Name = "ForceFull";
            this.ForceFull.Size = new System.Drawing.Size(108, 17);
            this.ForceFull.TabIndex = 6;
            this.ForceFull.Text = "Force full backup";
            this.ForceFull.UseVisualStyleBackColor = true;
            // 
            // RunNowFinished
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ForceFull);
            this.Controls.Add(this.Summary);
            this.Controls.Add(this.label1);
            this.Name = "RunNowFinished";
            this.Size = new System.Drawing.Size(506, 242);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.TextBox Summary;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox ForceFull;
    }
}
