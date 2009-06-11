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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FilterDialog));
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
            resources.ApplyResources(this.Inclusive, "Inclusive");
            this.Inclusive.Checked = true;
            this.Inclusive.Name = "Inclusive";
            this.Inclusive.TabStop = true;
            this.Inclusive.UseVisualStyleBackColor = true;
            // 
            // Exclusive
            // 
            resources.ApplyResources(this.Exclusive, "Exclusive");
            this.Exclusive.Name = "Exclusive";
            this.Exclusive.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // FilterText
            // 
            resources.ApplyResources(this.FilterText, "FilterText");
            this.FilterText.Name = "FilterText";
            // 
            // OKBtn
            // 
            resources.ApplyResources(this.OKBtn, "OKBtn");
            this.OKBtn.Name = "OKBtn";
            this.OKBtn.UseVisualStyleBackColor = true;
            this.OKBtn.Click += new System.EventHandler(this.OKBtn_Click);
            // 
            // CancelBtn
            // 
            this.CancelBtn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(this.CancelBtn, "CancelBtn");
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.UseVisualStyleBackColor = true;
            // 
            // IsRegExp
            // 
            resources.ApplyResources(this.IsRegExp, "IsRegExp");
            this.IsRegExp.Checked = true;
            this.IsRegExp.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IsRegExp.Name = "IsRegExp";
            this.IsRegExp.UseVisualStyleBackColor = true;
            // 
            // FilterDialog
            // 
            this.AcceptButton = this.OKBtn;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.Controls.Add(this.FilterText);
            this.Controls.Add(this.IsRegExp);
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Exclusive);
            this.Controls.Add(this.Inclusive);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "FilterDialog";
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