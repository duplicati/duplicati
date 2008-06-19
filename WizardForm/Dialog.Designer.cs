namespace System.Windows.Forms.Wizard
{
    partial class Dialog
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
            this._InfoPanel = new System.Windows.Forms.Panel();
            this._InfoLabel = new System.Windows.Forms.Label();
            this._TitleLabel = new System.Windows.Forms.Label();
            this._PageIcon = new System.Windows.Forms.PictureBox();
            this._ButtonPanel = new System.Windows.Forms.Panel();
            this._BackButton = new System.Windows.Forms.Button();
            this._NextButton = new System.Windows.Forms.Button();
            this._CancelButton = new System.Windows.Forms.Button();
            this._ContentPanel = new System.Windows.Forms.GroupBox();
            this._InfoPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._PageIcon)).BeginInit();
            this._ButtonPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // _InfoPanel
            // 
            this._InfoPanel.BackColor = System.Drawing.SystemColors.Window;
            this._InfoPanel.Controls.Add(this._InfoLabel);
            this._InfoPanel.Controls.Add(this._TitleLabel);
            this._InfoPanel.Controls.Add(this._PageIcon);
            this._InfoPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this._InfoPanel.Location = new System.Drawing.Point(0, 0);
            this._InfoPanel.Name = "_InfoPanel";
            this._InfoPanel.Size = new System.Drawing.Size(506, 64);
            this._InfoPanel.TabIndex = 0;
            // 
            // _InfoLabel
            // 
            this._InfoLabel.Location = new System.Drawing.Point(40, 24);
            this._InfoLabel.Name = "_InfoLabel";
            this._InfoLabel.Size = new System.Drawing.Size(400, 32);
            this._InfoLabel.TabIndex = 2;
            this._InfoLabel.Text = "Helptext";
            // 
            // _TitleLabel
            // 
            this._TitleLabel.AutoSize = true;
            this._TitleLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._TitleLabel.Location = new System.Drawing.Point(16, 8);
            this._TitleLabel.Name = "_TitleLabel";
            this._TitleLabel.Size = new System.Drawing.Size(32, 13);
            this._TitleLabel.TabIndex = 1;
            this._TitleLabel.Text = "Title";
            // 
            // _PageIcon
            // 
            this._PageIcon.Location = new System.Drawing.Point(448, 8);
            this._PageIcon.Name = "_PageIcon";
            this._PageIcon.Size = new System.Drawing.Size(48, 48);
            this._PageIcon.TabIndex = 0;
            this._PageIcon.TabStop = false;
            // 
            // _ButtonPanel
            // 
            this._ButtonPanel.Controls.Add(this._BackButton);
            this._ButtonPanel.Controls.Add(this._NextButton);
            this._ButtonPanel.Controls.Add(this._CancelButton);
            this._ButtonPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this._ButtonPanel.Location = new System.Drawing.Point(0, 306);
            this._ButtonPanel.Name = "_ButtonPanel";
            this._ButtonPanel.Size = new System.Drawing.Size(506, 48);
            this._ButtonPanel.TabIndex = 1;
            // 
            // _BackButton
            // 
            this._BackButton.Location = new System.Drawing.Point(248, 16);
            this._BackButton.Name = "_BackButton";
            this._BackButton.Size = new System.Drawing.Size(80, 24);
            this._BackButton.TabIndex = 2;
            this._BackButton.Text = "< Back";
            this._BackButton.UseVisualStyleBackColor = true;
            this._BackButton.Click += new System.EventHandler(this.BackBtn_Click);
            // 
            // _NextButton
            // 
            this._NextButton.Location = new System.Drawing.Point(328, 16);
            this._NextButton.Name = "_NextButton";
            this._NextButton.Size = new System.Drawing.Size(80, 24);
            this._NextButton.TabIndex = 1;
            this._NextButton.Text = "Next >";
            this._NextButton.UseVisualStyleBackColor = true;
            this._NextButton.Click += new System.EventHandler(this.NextBtn_Click);
            // 
            // _CancelButton
            // 
            this._CancelButton.Location = new System.Drawing.Point(416, 16);
            this._CancelButton.Name = "_CancelButton";
            this._CancelButton.Size = new System.Drawing.Size(80, 24);
            this._CancelButton.TabIndex = 0;
            this._CancelButton.Text = "Cancel";
            this._CancelButton.UseVisualStyleBackColor = true;
            this._CancelButton.Click += new System.EventHandler(this.CancelBtn_Click);
            // 
            // _ContentPanel
            // 
            this._ContentPanel.Location = new System.Drawing.Point(-1, 61);
            this._ContentPanel.Name = "_ContentPanel";
            this._ContentPanel.Size = new System.Drawing.Size(509, 244);
            this._ContentPanel.TabIndex = 2;
            this._ContentPanel.TabStop = false;
            // 
            // Dialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(506, 354);
            this.Controls.Add(this._ButtonPanel);
            this.Controls.Add(this._InfoPanel);
            this.Controls.Add(this._ContentPanel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Dialog";
            this.Text = "Dialog";
            this.Load += new System.EventHandler(this.Dialog_Load);
            this._InfoPanel.ResumeLayout(false);
            this._InfoPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this._PageIcon)).EndInit();
            this._ButtonPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private Panel _InfoPanel;
        private PictureBox _PageIcon;
        private Panel _ButtonPanel;
        private Button _BackButton;
        private Button _NextButton;
        private Button _CancelButton;
        private Label _InfoLabel;
        private Label _TitleLabel;
        private GroupBox _ContentPanel;
    }
}