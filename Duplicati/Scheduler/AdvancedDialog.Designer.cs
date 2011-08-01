namespace Duplicati.Scheduler
{
    partial class AdvancedDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AdvancedDialog));
            this.OrphanCheckBox = new System.Windows.Forms.CheckBox();
            this.MapCheckBox = new System.Windows.Forms.CheckBox();
            this.CanButton = new System.Windows.Forms.Button();
            this.OKButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // OrphanCheckBox
            // 
            this.OrphanCheckBox.AutoSize = true;
            this.OrphanCheckBox.Checked = true;
            this.OrphanCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.OrphanCheckBox.Location = new System.Drawing.Point(13, 28);
            this.OrphanCheckBox.Name = "OrphanCheckBox";
            this.OrphanCheckBox.Size = new System.Drawing.Size(15, 14);
            this.OrphanCheckBox.TabIndex = 0;
            this.OrphanCheckBox.UseVisualStyleBackColor = true;
            // 
            // MapCheckBox
            // 
            this.MapCheckBox.AutoSize = true;
            this.MapCheckBox.Checked = true;
            this.MapCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.MapCheckBox.Location = new System.Drawing.Point(13, 82);
            this.MapCheckBox.Name = "MapCheckBox";
            this.MapCheckBox.Size = new System.Drawing.Size(305, 23);
            this.MapCheckBox.TabIndex = 2;
            this.MapCheckBox.Text = "Map network drives when backups run.";
            this.MapCheckBox.UseVisualStyleBackColor = true;
            // 
            // CanButton
            // 
            this.CanButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CanButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CanButton.Location = new System.Drawing.Point(255, 134);
            this.CanButton.Name = "CanButton";
            this.CanButton.Size = new System.Drawing.Size(75, 31);
            this.CanButton.TabIndex = 3;
            this.CanButton.Text = "Cancel";
            this.CanButton.UseVisualStyleBackColor = true;
            this.CanButton.Click += new System.EventHandler(this.CanButton_Click);
            // 
            // OKButton
            // 
            this.OKButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.OKButton.Location = new System.Drawing.Point(174, 134);
            this.OKButton.Name = "OKButton";
            this.OKButton.Size = new System.Drawing.Size(75, 31);
            this.OKButton.TabIndex = 4;
            this.OKButton.Text = "OK";
            this.OKButton.UseVisualStyleBackColor = true;
            this.OKButton.Click += new System.EventHandler(this.OKButton_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(31, 25);
            this.label1.MaximumSize = new System.Drawing.Size(320, 40);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(294, 38);
            this.label1.TabIndex = 5;
            this.label1.Text = "Remove incremental scans that have no corresponding full scan.";
            // 
            // AdvancedDialog
            // 
            this.AcceptButton = this.OKButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Gainsboro;
            this.CancelButton = this.CanButton;
            this.ClientSize = new System.Drawing.Size(343, 169);
            this.ControlBox = false;
            this.Controls.Add(this.label1);
            this.Controls.Add(this.OKButton);
            this.Controls.Add(this.CanButton);
            this.Controls.Add(this.MapCheckBox);
            this.Controls.Add(this.OrphanCheckBox);
            this.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "AdvancedDialog";
            this.Text = "Advanced";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox OrphanCheckBox;
        private System.Windows.Forms.CheckBox MapCheckBox;
        private System.Windows.Forms.Button CanButton;
        private System.Windows.Forms.Button OKButton;
        private System.Windows.Forms.Label label1;
    }
}