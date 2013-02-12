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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Dialog));
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
            resources.ApplyResources(this._InfoPanel, "_InfoPanel");
            this._InfoPanel.Name = "_InfoPanel";
            // 
            // _InfoLabel
            // 
            resources.ApplyResources(this._InfoLabel, "_InfoLabel");
            this._InfoLabel.Name = "_InfoLabel";
            // 
            // _TitleLabel
            // 
            resources.ApplyResources(this._TitleLabel, "_TitleLabel");
            this._TitleLabel.Name = "_TitleLabel";
            // 
            // _PageIcon
            // 
            resources.ApplyResources(this._PageIcon, "_PageIcon");
            this._PageIcon.Name = "_PageIcon";
            this._PageIcon.TabStop = false;
            // 
            // _ButtonPanel
            // 
            this._ButtonPanel.Controls.Add(this._BackButton);
            this._ButtonPanel.Controls.Add(this._NextButton);
            this._ButtonPanel.Controls.Add(this._CancelButton);
            resources.ApplyResources(this._ButtonPanel, "_ButtonPanel");
            this._ButtonPanel.Name = "_ButtonPanel";
            // 
            // _BackButton
            // 
            resources.ApplyResources(this._BackButton, "_BackButton");
            this._BackButton.Name = "_BackButton";
            this._BackButton.UseVisualStyleBackColor = true;
            this._BackButton.Click += new System.EventHandler(this.BackBtn_Click);
            // 
            // _NextButton
            // 
            resources.ApplyResources(this._NextButton, "_NextButton");
            this._NextButton.Name = "_NextButton";
            this._NextButton.UseVisualStyleBackColor = true;
            this._NextButton.Click += new System.EventHandler(this.NextBtn_Click);
            // 
            // _CancelButton
            // 
            this._CancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(this._CancelButton, "_CancelButton");
            this._CancelButton.Name = "_CancelButton";
            this._CancelButton.UseVisualStyleBackColor = true;
            this._CancelButton.Click += new System.EventHandler(this.CancelBtn_Click);
            // 
            // _ContentPanel
            // 
            resources.ApplyResources(this._ContentPanel, "_ContentPanel");
            this._ContentPanel.Name = "_ContentPanel";
            this._ContentPanel.TabStop = false;
            // 
            // Dialog
            // 
            this.AcceptButton = this._NextButton;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this._CancelButton;
            this.Controls.Add(this._ButtonPanel);
            this.Controls.Add(this._InfoPanel);
            this.Controls.Add(this._ContentPanel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Dialog";
            this.Load += new System.EventHandler(this.Dialog_Load);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.Dialog_KeyUp);
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