namespace Duplicati.Wizard_pages.Add_backup
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
            this.label1 = new System.Windows.Forms.Label();
            this.Summary = new System.Windows.Forms.TextBox();
            this.RunNow = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(24, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(50, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Summary";
            // 
            // Summary
            // 
            this.Summary.Font = new System.Drawing.Font("Courier New", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Summary.Location = new System.Drawing.Point(24, 40);
            this.Summary.Multiline = true;
            this.Summary.Name = "Summary";
            this.Summary.ReadOnly = true;
            this.Summary.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.Summary.Size = new System.Drawing.Size(456, 152);
            this.Summary.TabIndex = 1;
            // 
            // RunNow
            // 
            this.RunNow.AutoSize = true;
            this.RunNow.Location = new System.Drawing.Point(24, 200);
            this.RunNow.Name = "RunNow";
            this.RunNow.Size = new System.Drawing.Size(108, 17);
            this.RunNow.TabIndex = 2;
            this.RunNow.Text = "Run backup now";
            this.RunNow.UseVisualStyleBackColor = true;
            // 
            // FinishedAdd
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RunNow);
            this.Controls.Add(this.Summary);
            this.Controls.Add(this.label1);
            this.Name = "FinishedAdd";
            this.Size = new System.Drawing.Size(506, 242);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        public System.Windows.Forms.TextBox Summary;
        public System.Windows.Forms.CheckBox RunNow;
    }
}
