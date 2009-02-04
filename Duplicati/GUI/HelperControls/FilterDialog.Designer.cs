namespace Duplicati.GUI.HelperControls
{
    partial class FilterDialog
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.Inclusive = new System.Windows.Forms.RadioButton();
            this.Exclusive = new System.Windows.Forms.RadioButton();
            this.label1 = new System.Windows.Forms.Label();
            this.FilterText = new System.Windows.Forms.TextBox();
            this.OKBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.IsRegExp = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // Inclusive
            // 
            this.Inclusive.AutoSize = true;
            this.Inclusive.Checked = true;
            this.Inclusive.Location = new System.Drawing.Point(8, 8);
            this.Inclusive.Name = "Inclusive";
            this.Inclusive.Size = new System.Drawing.Size(167, 17);
            this.Inclusive.TabIndex = 0;
            this.Inclusive.TabStop = true;
            this.Inclusive.Text = "Include files matching the filter";
            this.Inclusive.UseVisualStyleBackColor = true;
            // 
            // Exclusive
            // 
            this.Exclusive.AutoSize = true;
            this.Exclusive.Location = new System.Drawing.Point(8, 32);
            this.Exclusive.Name = "Exclusive";
            this.Exclusive.Size = new System.Drawing.Size(170, 17);
            this.Exclusive.TabIndex = 1;
            this.Exclusive.Text = "Exclude files matching the filter";
            this.Exclusive.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 64);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(29, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Filter";
            // 
            // FilterText
            // 
            this.FilterText.Location = new System.Drawing.Point(56, 64);
            this.FilterText.Name = "FilterText";
            this.FilterText.Size = new System.Drawing.Size(320, 20);
            this.FilterText.TabIndex = 3;
            // 
            // OKBtn
            // 
            this.OKBtn.Location = new System.Drawing.Point(112, 96);
            this.OKBtn.Name = "OKBtn";
            this.OKBtn.Size = new System.Drawing.Size(75, 23);
            this.OKBtn.TabIndex = 4;
            this.OKBtn.Text = "OK";
            this.OKBtn.UseVisualStyleBackColor = true;
            this.OKBtn.Click += new System.EventHandler(this.OKBtn_Click);
            // 
            // CancelBtn
            // 
            this.CancelBtn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBtn.Location = new System.Drawing.Point(200, 96);
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.Size = new System.Drawing.Size(75, 23);
            this.CancelBtn.TabIndex = 5;
            this.CancelBtn.Text = "Cancel";
            this.CancelBtn.UseVisualStyleBackColor = true;
            // 
            // IsRegExp
            // 
            this.IsRegExp.AutoSize = true;
            this.IsRegExp.Checked = true;
            this.IsRegExp.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IsRegExp.Location = new System.Drawing.Point(224, 48);
            this.IsRegExp.Name = "IsRegExp";
            this.IsRegExp.Size = new System.Drawing.Size(146, 17);
            this.IsRegExp.TabIndex = 6;
            this.IsRegExp.Text = "Filter is regular expression";
            this.IsRegExp.UseVisualStyleBackColor = true;
            // 
            // FilterDialog
            // 
            this.AcceptButton = this.OKBtn;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.ClientSize = new System.Drawing.Size(389, 126);
            this.Controls.Add(this.FilterText);
            this.Controls.Add(this.IsRegExp);
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Exclusive);
            this.Controls.Add(this.Inclusive);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "FilterDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "FilterDialog";
            this.Load += new System.EventHandler(this.FilterDialog_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RadioButton Inclusive;
        private System.Windows.Forms.RadioButton Exclusive;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox FilterText;
        private System.Windows.Forms.Button OKBtn;
        private System.Windows.Forms.Button CancelBtn;
        private System.Windows.Forms.CheckBox IsRegExp;
    }
}