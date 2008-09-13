namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class SelectWhen
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
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.label3 = new System.Windows.Forms.Label();
            this.RepeatInterval = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.EnableRepeat = new System.Windows.Forms.CheckBox();
            this.OffsetTime = new System.Windows.Forms.DateTimePicker();
            this.OffsetDate = new System.Windows.Forms.DateTimePicker();
            this.label1 = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.RepeatInterval);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Location = new System.Drawing.Point(24, 40);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(448, 88);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            // 
            // label3
            // 
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(16, 48);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(416, 32);
            this.label3.TabIndex = 2;
            this.label3.Text = "Time is entered as 2D for 2 days, and 1M3D for one month, three days. Valid types" +
                " are s,m,h,D,W,M and Y.";
            // 
            // RepeatInterval
            // 
            this.RepeatInterval.Location = new System.Drawing.Point(120, 24);
            this.RepeatInterval.Name = "RepeatInterval";
            this.RepeatInterval.Size = new System.Drawing.Size(312, 20);
            this.RepeatInterval.TabIndex = 1;
            this.RepeatInterval.Text = "1D";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 24);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(69, 13);
            this.label2.TabIndex = 0;
            this.label2.Text = "Repeat each";
            // 
            // EnableRepeat
            // 
            this.EnableRepeat.AutoSize = true;
            this.EnableRepeat.Checked = true;
            this.EnableRepeat.CheckState = System.Windows.Forms.CheckState.Checked;
            this.EnableRepeat.Location = new System.Drawing.Point(40, 40);
            this.EnableRepeat.Name = "EnableRepeat";
            this.EnableRepeat.Size = new System.Drawing.Size(160, 17);
            this.EnableRepeat.TabIndex = 1;
            this.EnableRepeat.Text = "Repeat the backup regularly";
            this.EnableRepeat.UseVisualStyleBackColor = true;
            // 
            // OffsetTime
            // 
            this.OffsetTime.Format = System.Windows.Forms.DateTimePickerFormat.Time;
            this.OffsetTime.Location = new System.Drawing.Point(312, 8);
            this.OffsetTime.Name = "OffsetTime";
            this.OffsetTime.ShowUpDown = true;
            this.OffsetTime.Size = new System.Drawing.Size(80, 20);
            this.OffsetTime.TabIndex = 5;
            this.OffsetTime.ValueChanged += new System.EventHandler(this.OffsetTime_ValueChanged);
            // 
            // OffsetDate
            // 
            this.OffsetDate.Location = new System.Drawing.Point(176, 8);
            this.OffsetDate.Name = "OffsetDate";
            this.OffsetDate.Size = new System.Drawing.Size(128, 20);
            this.OffsetDate.TabIndex = 4;
            this.OffsetDate.ValueChanged += new System.EventHandler(this.OffsetDate_ValueChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(24, 8);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(137, 13);
            this.label1.TabIndex = 3;
            this.label1.Text = "Run the backup at this time";
            // 
            // SelectWhen
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.OffsetTime);
            this.Controls.Add(this.EnableRepeat);
            this.Controls.Add(this.OffsetDate);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.groupBox1);
            this.Name = "SelectWhen";
            this.Size = new System.Drawing.Size(506, 242);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.CheckBox EnableRepeat;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox RepeatInterval;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.DateTimePicker OffsetTime;
        private System.Windows.Forms.DateTimePicker OffsetDate;
        private System.Windows.Forms.Label label1;
    }
}
