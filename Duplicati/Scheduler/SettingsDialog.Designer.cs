namespace Duplicati.Scheduler
{
    partial class SettingsDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsDialog));
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.RemoveLabel = new System.Windows.Forms.Label();
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            this.label2 = new System.Windows.Forms.Label();
            this.CanButton = new System.Windows.Forms.Button();
            this.OKButton = new System.Windows.Forms.Button();
            this.BubbleCheckBox = new System.Windows.Forms.CheckBox();
            this.passwordControl1 = new Duplicati.Scheduler.PasswordControl();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            this.SuspendLayout();
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Location = new System.Drawing.Point(12, 12);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(269, 23);
            this.checkBox1.TabIndex = 0;
            this.checkBox1.Text = "Use this password for all backups.";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged);
            // 
            // RemoveLabel
            // 
            this.RemoveLabel.AutoSize = true;
            this.RemoveLabel.Location = new System.Drawing.Point(27, 231);
            this.RemoveLabel.Name = "RemoveLabel";
            this.RemoveLabel.Size = new System.Drawing.Size(227, 19);
            this.RemoveLabel.TabIndex = 2;
            this.RemoveLabel.Text = "Remove log entries older than ";
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.Location = new System.Drawing.Point(260, 227);
            this.numericUpDown1.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(73, 27);
            this.numericUpDown1.TabIndex = 3;
            this.numericUpDown1.Value = new decimal(new int[] {
            90,
            0,
            0,
            0});
            this.numericUpDown1.ValueChanged += new System.EventHandler(this.numericUpDown1_ValueChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(339, 229);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(46, 19);
            this.label2.TabIndex = 4;
            this.label2.Text = "days.";
            // 
            // CanButton
            // 
            this.CanButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CanButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CanButton.Location = new System.Drawing.Point(473, 272);
            this.CanButton.Name = "CanButton";
            this.CanButton.Size = new System.Drawing.Size(75, 33);
            this.CanButton.TabIndex = 5;
            this.CanButton.Text = "Cancel";
            this.CanButton.UseVisualStyleBackColor = true;
            this.CanButton.Click += new System.EventHandler(this.CanButton_Click);
            // 
            // OKButton
            // 
            this.OKButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.OKButton.Location = new System.Drawing.Point(392, 272);
            this.OKButton.Name = "OKButton";
            this.OKButton.Size = new System.Drawing.Size(75, 33);
            this.OKButton.TabIndex = 6;
            this.OKButton.Text = "OK";
            this.OKButton.UseVisualStyleBackColor = true;
            this.OKButton.Click += new System.EventHandler(this.OKButton_Click);
            // 
            // BubbleCheckBox
            // 
            this.BubbleCheckBox.AutoSize = true;
            this.BubbleCheckBox.Location = new System.Drawing.Point(31, 274);
            this.BubbleCheckBox.Name = "BubbleCheckBox";
            this.BubbleCheckBox.Size = new System.Drawing.Size(361, 23);
            this.BubbleCheckBox.TabIndex = 7;
            this.BubbleCheckBox.Text = "Show backup status in toolbar notifier bubbles.";
            this.BubbleCheckBox.UseVisualStyleBackColor = true;
            this.BubbleCheckBox.Visible = false;
            // 
            // passwordControl1
            // 
            this.passwordControl1.BackColor = System.Drawing.Color.Gainsboro;
            this.passwordControl1.CheckMod = "";
            this.passwordControl1.Checksum = new byte[] {
        ((byte)(1)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(208)),
        ((byte)(140)),
        ((byte)(157)),
        ((byte)(223)),
        ((byte)(1)),
        ((byte)(21)),
        ((byte)(209)),
        ((byte)(17)),
        ((byte)(140)),
        ((byte)(122)),
        ((byte)(0)),
        ((byte)(192)),
        ((byte)(79)),
        ((byte)(194)),
        ((byte)(151)),
        ((byte)(235)),
        ((byte)(1)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(48)),
        ((byte)(183)),
        ((byte)(91)),
        ((byte)(129)),
        ((byte)(98)),
        ((byte)(50)),
        ((byte)(81)),
        ((byte)(67)),
        ((byte)(164)),
        ((byte)(141)),
        ((byte)(23)),
        ((byte)(21)),
        ((byte)(92)),
        ((byte)(22)),
        ((byte)(163)),
        ((byte)(166)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(2)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(16)),
        ((byte)(102)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(1)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(32)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(230)),
        ((byte)(20)),
        ((byte)(12)),
        ((byte)(27)),
        ((byte)(88)),
        ((byte)(176)),
        ((byte)(90)),
        ((byte)(225)),
        ((byte)(54)),
        ((byte)(47)),
        ((byte)(201)),
        ((byte)(171)),
        ((byte)(23)),
        ((byte)(240)),
        ((byte)(125)),
        ((byte)(95)),
        ((byte)(34)),
        ((byte)(28)),
        ((byte)(80)),
        ((byte)(198)),
        ((byte)(72)),
        ((byte)(136)),
        ((byte)(121)),
        ((byte)(56)),
        ((byte)(135)),
        ((byte)(95)),
        ((byte)(13)),
        ((byte)(240)),
        ((byte)(4)),
        ((byte)(242)),
        ((byte)(32)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(14)),
        ((byte)(128)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(2)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(32)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(115)),
        ((byte)(213)),
        ((byte)(202)),
        ((byte)(86)),
        ((byte)(242)),
        ((byte)(62)),
        ((byte)(219)),
        ((byte)(207)),
        ((byte)(39)),
        ((byte)(54)),
        ((byte)(126)),
        ((byte)(232)),
        ((byte)(45)),
        ((byte)(168)),
        ((byte)(8)),
        ((byte)(169)),
        ((byte)(11)),
        ((byte)(205)),
        ((byte)(229)),
        ((byte)(214)),
        ((byte)(254)),
        ((byte)(108)),
        ((byte)(2)),
        ((byte)(65)),
        ((byte)(19)),
        ((byte)(51)),
        ((byte)(26)),
        ((byte)(216)),
        ((byte)(132)),
        ((byte)(41)),
        ((byte)(123)),
        ((byte)(116)),
        ((byte)(144)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(120)),
        ((byte)(234)),
        ((byte)(183)),
        ((byte)(150)),
        ((byte)(107)),
        ((byte)(36)),
        ((byte)(213)),
        ((byte)(47)),
        ((byte)(80)),
        ((byte)(14)),
        ((byte)(118)),
        ((byte)(205)),
        ((byte)(4)),
        ((byte)(54)),
        ((byte)(169)),
        ((byte)(85)),
        ((byte)(137)),
        ((byte)(127)),
        ((byte)(213)),
        ((byte)(201)),
        ((byte)(178)),
        ((byte)(106)),
        ((byte)(77)),
        ((byte)(165)),
        ((byte)(50)),
        ((byte)(55)),
        ((byte)(141)),
        ((byte)(115)),
        ((byte)(123)),
        ((byte)(37)),
        ((byte)(144)),
        ((byte)(104)),
        ((byte)(81)),
        ((byte)(164)),
        ((byte)(134)),
        ((byte)(32)),
        ((byte)(101)),
        ((byte)(240)),
        ((byte)(58)),
        ((byte)(188)),
        ((byte)(146)),
        ((byte)(137)),
        ((byte)(119)),
        ((byte)(154)),
        ((byte)(94)),
        ((byte)(69)),
        ((byte)(154)),
        ((byte)(5)),
        ((byte)(52)),
        ((byte)(155)),
        ((byte)(219)),
        ((byte)(48)),
        ((byte)(64)),
        ((byte)(112)),
        ((byte)(91)),
        ((byte)(31)),
        ((byte)(39)),
        ((byte)(142)),
        ((byte)(200)),
        ((byte)(190)),
        ((byte)(30)),
        ((byte)(65)),
        ((byte)(105)),
        ((byte)(218)),
        ((byte)(106)),
        ((byte)(169)),
        ((byte)(119)),
        ((byte)(228)),
        ((byte)(109)),
        ((byte)(236)),
        ((byte)(60)),
        ((byte)(186)),
        ((byte)(151)),
        ((byte)(86)),
        ((byte)(225)),
        ((byte)(69)),
        ((byte)(92)),
        ((byte)(2)),
        ((byte)(169)),
        ((byte)(65)),
        ((byte)(210)),
        ((byte)(248)),
        ((byte)(115)),
        ((byte)(177)),
        ((byte)(239)),
        ((byte)(15)),
        ((byte)(75)),
        ((byte)(15)),
        ((byte)(118)),
        ((byte)(207)),
        ((byte)(77)),
        ((byte)(84)),
        ((byte)(44)),
        ((byte)(111)),
        ((byte)(202)),
        ((byte)(242)),
        ((byte)(162)),
        ((byte)(180)),
        ((byte)(118)),
        ((byte)(85)),
        ((byte)(13)),
        ((byte)(158)),
        ((byte)(225)),
        ((byte)(47)),
        ((byte)(239)),
        ((byte)(140)),
        ((byte)(238)),
        ((byte)(156)),
        ((byte)(5)),
        ((byte)(97)),
        ((byte)(224)),
        ((byte)(104)),
        ((byte)(136)),
        ((byte)(87)),
        ((byte)(25)),
        ((byte)(136)),
        ((byte)(68)),
        ((byte)(37)),
        ((byte)(254)),
        ((byte)(163)),
        ((byte)(103)),
        ((byte)(133)),
        ((byte)(152)),
        ((byte)(113)),
        ((byte)(113)),
        ((byte)(70)),
        ((byte)(100)),
        ((byte)(85)),
        ((byte)(0)),
        ((byte)(152)),
        ((byte)(103)),
        ((byte)(159)),
        ((byte)(233)),
        ((byte)(225)),
        ((byte)(134)),
        ((byte)(70)),
        ((byte)(92)),
        ((byte)(178)),
        ((byte)(62)),
        ((byte)(45)),
        ((byte)(145)),
        ((byte)(228)),
        ((byte)(160)),
        ((byte)(103)),
        ((byte)(64)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(164)),
        ((byte)(254)),
        ((byte)(131)),
        ((byte)(153)),
        ((byte)(161)),
        ((byte)(3)),
        ((byte)(37)),
        ((byte)(154)),
        ((byte)(74)),
        ((byte)(35)),
        ((byte)(34)),
        ((byte)(48)),
        ((byte)(90)),
        ((byte)(239)),
        ((byte)(43)),
        ((byte)(253)),
        ((byte)(130)),
        ((byte)(255)),
        ((byte)(5)),
        ((byte)(108)),
        ((byte)(236)),
        ((byte)(239)),
        ((byte)(244)),
        ((byte)(105)),
        ((byte)(138)),
        ((byte)(130)),
        ((byte)(182)),
        ((byte)(176)),
        ((byte)(85)),
        ((byte)(108)),
        ((byte)(134)),
        ((byte)(156)),
        ((byte)(243)),
        ((byte)(67)),
        ((byte)(9)),
        ((byte)(198)),
        ((byte)(186)),
        ((byte)(73)),
        ((byte)(166)),
        ((byte)(120)),
        ((byte)(47)),
        ((byte)(50)),
        ((byte)(197)),
        ((byte)(154)),
        ((byte)(228)),
        ((byte)(78)),
        ((byte)(154)),
        ((byte)(158)),
        ((byte)(42)),
        ((byte)(168)),
        ((byte)(187)),
        ((byte)(252)),
        ((byte)(146)),
        ((byte)(60)),
        ((byte)(146)),
        ((byte)(187)),
        ((byte)(98)),
        ((byte)(43)),
        ((byte)(31)),
        ((byte)(229)),
        ((byte)(31)),
        ((byte)(253)),
        ((byte)(151)),
        ((byte)(168))};
            this.passwordControl1.Enabled = false;
            this.passwordControl1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.passwordControl1.Location = new System.Drawing.Point(30, 42);
            this.passwordControl1.Margin = new System.Windows.Forms.Padding(4);
            this.passwordControl1.Name = "passwordControl1";
            this.passwordControl1.Size = new System.Drawing.Size(517, 165);
            this.passwordControl1.TabIndex = 1;
            // 
            // SettingsDialog
            // 
            this.AcceptButton = this.OKButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Gainsboro;
            this.CancelButton = this.CanButton;
            this.ClientSize = new System.Drawing.Size(560, 317);
            this.Controls.Add(this.BubbleCheckBox);
            this.Controls.Add(this.OKButton);
            this.Controls.Add(this.CanButton);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.numericUpDown1);
            this.Controls.Add(this.RemoveLabel);
            this.Controls.Add(this.passwordControl1);
            this.Controls.Add(this.checkBox1);
            this.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "SettingsDialog";
            this.Text = "Settings";
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox checkBox1;
        private PasswordControl passwordControl1;
        private System.Windows.Forms.Label RemoveLabel;
        private System.Windows.Forms.NumericUpDown numericUpDown1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button CanButton;
        private System.Windows.Forms.Button OKButton;
        private System.Windows.Forms.CheckBox BubbleCheckBox;
    }
}