namespace Duplicati.Scheduler
{
    partial class JobSummary
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
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.Label label3;
            System.Windows.Forms.Label label2;
            System.Windows.Forms.Label nameLabel;
            System.Windows.Forms.Label sourceLabel;
            System.Windows.Forms.Label destinationLabel;
            System.Windows.Forms.Label asLabel;
            System.Windows.Forms.Label fullRepeatStrLabel;
            System.Windows.Forms.Label label1;
            System.Windows.Forms.Label label4;
            this.MaxAgeTextBox = new System.Windows.Forms.RichTextBox();
            this.MaxFullTextBox = new System.Windows.Forms.RichTextBox();
            this.nameTextBox = new System.Windows.Forms.RichTextBox();
            this.sourceTextBox = new System.Windows.Forms.RichTextBox();
            this.destinationTextBox = new System.Windows.Forms.RichTextBox();
            this.DescriptionTextBox = new System.Windows.Forms.RichTextBox();
            this.fullRepeatStrTextBox = new System.Windows.Forms.RichTextBox();
            this.SourceErrorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            this.DestErrorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            this.EnableLabel = new System.Windows.Forms.Label();
            this.PassRichTextBox = new System.Windows.Forms.RichTextBox();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.panel1 = new System.Windows.Forms.Panel();
            this.listBox1 = new System.Windows.Forms.ListBox();
            this.LastModLabel = new System.Windows.Forms.Label();
            label3 = new System.Windows.Forms.Label();
            label2 = new System.Windows.Forms.Label();
            nameLabel = new System.Windows.Forms.Label();
            sourceLabel = new System.Windows.Forms.Label();
            destinationLabel = new System.Windows.Forms.Label();
            asLabel = new System.Windows.Forms.Label();
            fullRepeatStrLabel = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            label4 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.SourceErrorProvider)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.DestErrorProvider)).BeginInit();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(45, 277);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(219, 19);
            label3.TabIndex = 35;
            label3.Text = "Delete full backups older than";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(216, 244);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(48, 19);
            label2.TabIndex = 32;
            label2.Text = "Keep ";
            // 
            // nameLabel
            // 
            nameLabel.AutoSize = true;
            nameLabel.Location = new System.Drawing.Point(208, 18);
            nameLabel.Name = "nameLabel";
            nameLabel.Size = new System.Drawing.Size(56, 19);
            nameLabel.TabIndex = 22;
            nameLabel.Text = "Name:";
            // 
            // sourceLabel
            // 
            sourceLabel.AutoSize = true;
            sourceLabel.Location = new System.Drawing.Point(128, 51);
            sourceLabel.Name = "sourceLabel";
            sourceLabel.Size = new System.Drawing.Size(136, 19);
            sourceLabel.TabIndex = 24;
            sourceLabel.Text = "Backup files from ";
            // 
            // destinationLabel
            // 
            destinationLabel.AutoSize = true;
            destinationLabel.Location = new System.Drawing.Point(147, 84);
            destinationLabel.Name = "destinationLabel";
            destinationLabel.Size = new System.Drawing.Size(117, 19);
            destinationLabel.TabIndex = 26;
            destinationLabel.Text = "Put backups in ";
            // 
            // asLabel
            // 
            asLabel.AutoSize = true;
            asLabel.Location = new System.Drawing.Point(167, 117);
            asLabel.Name = "asLabel";
            asLabel.Size = new System.Drawing.Size(97, 19);
            asLabel.TabIndex = 28;
            asLabel.Text = "Run backup ";
            // 
            // fullRepeatStrLabel
            // 
            fullRepeatStrLabel.AutoSize = true;
            fullRepeatStrLabel.Location = new System.Drawing.Point(103, 211);
            fullRepeatStrLabel.Name = "fullRepeatStrLabel";
            fullRepeatStrLabel.Size = new System.Drawing.Size(161, 19);
            fullRepeatStrLabel.TabIndex = 30;
            fullRepeatStrLabel.Text = "Do a full backup after";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(373, 244);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(98, 19);
            label1.TabIndex = 37;
            label1.Text = "full backups.";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new System.Drawing.Point(166, 309);
            label4.Name = "label4";
            label4.Size = new System.Drawing.Size(98, 19);
            label4.TabIndex = 39;
            label4.Text = "Backups are ";
            // 
            // MaxAgeTextBox
            // 
            this.MaxAgeTextBox.Location = new System.Drawing.Point(270, 274);
            this.MaxAgeTextBox.Name = "MaxAgeTextBox";
            this.MaxAgeTextBox.ReadOnly = true;
            this.MaxAgeTextBox.Size = new System.Drawing.Size(97, 27);
            this.MaxAgeTextBox.TabIndex = 34;
            this.MaxAgeTextBox.TabStop = false;
            this.MaxAgeTextBox.Text = "";
            // 
            // MaxFullTextBox
            // 
            this.MaxFullTextBox.Location = new System.Drawing.Point(270, 241);
            this.MaxFullTextBox.Name = "MaxFullTextBox";
            this.MaxFullTextBox.ReadOnly = true;
            this.MaxFullTextBox.Size = new System.Drawing.Size(97, 27);
            this.MaxFullTextBox.TabIndex = 33;
            this.MaxFullTextBox.TabStop = false;
            this.MaxFullTextBox.Text = "";
            // 
            // nameTextBox
            // 
            this.nameTextBox.Location = new System.Drawing.Point(270, 15);
            this.nameTextBox.Name = "nameTextBox";
            this.nameTextBox.ReadOnly = true;
            this.nameTextBox.Size = new System.Drawing.Size(238, 27);
            this.nameTextBox.TabIndex = 23;
            this.nameTextBox.TabStop = false;
            this.nameTextBox.Text = "";
            // 
            // sourceTextBox
            // 
            this.sourceTextBox.Location = new System.Drawing.Point(270, 48);
            this.sourceTextBox.Name = "sourceTextBox";
            this.sourceTextBox.ReadOnly = true;
            this.sourceTextBox.Size = new System.Drawing.Size(238, 27);
            this.sourceTextBox.TabIndex = 25;
            this.sourceTextBox.TabStop = false;
            this.sourceTextBox.Text = "";
            this.toolTip1.SetToolTip(this.sourceTextBox, "Double-click for a better view");
            this.sourceTextBox.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.sourceTextBox_MouseDoubleClick);
            // 
            // destinationTextBox
            // 
            this.destinationTextBox.DetectUrls = false;
            this.destinationTextBox.Location = new System.Drawing.Point(270, 81);
            this.destinationTextBox.Name = "destinationTextBox";
            this.destinationTextBox.ReadOnly = true;
            this.destinationTextBox.Size = new System.Drawing.Size(238, 27);
            this.destinationTextBox.TabIndex = 27;
            this.destinationTextBox.TabStop = false;
            this.destinationTextBox.Text = "";
            // 
            // DescriptionTextBox
            // 
            this.DescriptionTextBox.Location = new System.Drawing.Point(270, 114);
            this.DescriptionTextBox.Name = "DescriptionTextBox";
            this.DescriptionTextBox.ReadOnly = true;
            this.DescriptionTextBox.Size = new System.Drawing.Size(238, 83);
            this.DescriptionTextBox.TabIndex = 29;
            this.DescriptionTextBox.TabStop = false;
            this.DescriptionTextBox.Text = "";
            // 
            // fullRepeatStrTextBox
            // 
            this.fullRepeatStrTextBox.Location = new System.Drawing.Point(270, 208);
            this.fullRepeatStrTextBox.Name = "fullRepeatStrTextBox";
            this.fullRepeatStrTextBox.ReadOnly = true;
            this.fullRepeatStrTextBox.Size = new System.Drawing.Size(238, 27);
            this.fullRepeatStrTextBox.TabIndex = 31;
            this.fullRepeatStrTextBox.TabStop = false;
            this.fullRepeatStrTextBox.Text = "";
            // 
            // SourceErrorProvider
            // 
            this.SourceErrorProvider.ContainerControl = this;
            // 
            // DestErrorProvider
            // 
            this.DestErrorProvider.ContainerControl = this;
            // 
            // EnableLabel
            // 
            this.EnableLabel.AutoSize = true;
            this.EnableLabel.ForeColor = System.Drawing.Color.OrangeRed;
            this.EnableLabel.Location = new System.Drawing.Point(12, 18);
            this.EnableLabel.Name = "EnableLabel";
            this.EnableLabel.Size = new System.Drawing.Size(94, 19);
            this.EnableLabel.TabIndex = 36;
            this.EnableLabel.Text = "Not enabled";
            this.EnableLabel.Visible = false;
            // 
            // PassRichTextBox
            // 
            this.PassRichTextBox.BackColor = System.Drawing.SystemColors.Control;
            this.PassRichTextBox.Location = new System.Drawing.Point(270, 306);
            this.PassRichTextBox.Name = "PassRichTextBox";
            this.PassRichTextBox.ReadOnly = true;
            this.PassRichTextBox.Size = new System.Drawing.Size(238, 27);
            this.PassRichTextBox.TabIndex = 38;
            this.PassRichTextBox.TabStop = false;
            this.PassRichTextBox.Text = "";
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.listBox1);
            this.panel1.Location = new System.Drawing.Point(271, 51);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(237, 310);
            this.panel1.TabIndex = 40;
            this.panel1.Visible = false;
            // 
            // listBox1
            // 
            this.listBox1.BackColor = System.Drawing.SystemColors.Control;
            this.listBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listBox1.FormattingEnabled = true;
            this.listBox1.ItemHeight = 19;
            this.listBox1.Location = new System.Drawing.Point(0, 0);
            this.listBox1.Name = "listBox1";
            this.listBox1.Size = new System.Drawing.Size(237, 310);
            this.listBox1.TabIndex = 0;
            this.listBox1.Click += new System.EventHandler(this.listBox1_Click);
            // 
            // LastModLabel
            // 
            this.LastModLabel.AutoSize = true;
            this.LastModLabel.Location = new System.Drawing.Point(16, 337);
            this.LastModLabel.Name = "LastModLabel";
            this.LastModLabel.Size = new System.Drawing.Size(98, 19);
            this.LastModLabel.TabIndex = 41;
            this.LastModLabel.Text = "Never edited";
            // 
            // JobSummary
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.LastModLabel);
            this.Controls.Add(this.MaxAgeTextBox);
            this.Controls.Add(this.MaxFullTextBox);
            this.Controls.Add(this.PassRichTextBox);
            this.Controls.Add(this.fullRepeatStrTextBox);
            this.Controls.Add(this.DescriptionTextBox);
            this.Controls.Add(this.destinationTextBox);
            this.Controls.Add(this.sourceTextBox);
            this.Controls.Add(this.panel1);
            this.Controls.Add(label4);
            this.Controls.Add(label1);
            this.Controls.Add(this.EnableLabel);
            this.Controls.Add(label3);
            this.Controls.Add(label2);
            this.Controls.Add(nameLabel);
            this.Controls.Add(this.nameTextBox);
            this.Controls.Add(sourceLabel);
            this.Controls.Add(destinationLabel);
            this.Controls.Add(asLabel);
            this.Controls.Add(fullRepeatStrLabel);
            this.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "JobSummary";
            this.Size = new System.Drawing.Size(520, 364);
            ((System.ComponentModel.ISupportInitialize)(this.SourceErrorProvider)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.DestErrorProvider)).EndInit();
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RichTextBox MaxAgeTextBox;
        private System.Windows.Forms.RichTextBox MaxFullTextBox;
        private System.Windows.Forms.RichTextBox nameTextBox;
        private System.Windows.Forms.RichTextBox sourceTextBox;
        private System.Windows.Forms.RichTextBox destinationTextBox;
        private System.Windows.Forms.RichTextBox DescriptionTextBox;
        private System.Windows.Forms.RichTextBox fullRepeatStrTextBox;
        private System.Windows.Forms.ErrorProvider SourceErrorProvider;
        private System.Windows.Forms.ErrorProvider DestErrorProvider;
        private System.Windows.Forms.Label EnableLabel;
        private System.Windows.Forms.RichTextBox PassRichTextBox;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.ListBox listBox1;
        private System.Windows.Forms.Label LastModLabel;
    }
}
