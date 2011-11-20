namespace Duplicati.Scheduler
{
    partial class TaskEditControl
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskEditControl));
            this.EnableCheckBox = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.BeginDateTimePicker = new System.Windows.Forms.DateTimePicker();
            this.panel2 = new System.Windows.Forms.Panel();
            this.PeriodTabControl = new System.Windows.Forms.TabControl();
            this.TabOnce = new System.Windows.Forms.TabPage();
            this.OnceMonthCalendar = new System.Windows.Forms.MonthCalendar();
            this.label2 = new System.Windows.Forms.Label();
            this.TabDaily = new System.Windows.Forms.TabPage();
            this.label4 = new System.Windows.Forms.Label();
            this.DailyNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.label3 = new System.Windows.Forms.Label();
            this.TabWeekly = new System.Windows.Forms.TabPage();
            this.WeeklyGroupBox = new System.Windows.Forms.GroupBox();
            this.checkBox7 = new System.Windows.Forms.CheckBox();
            this.checkBox6 = new System.Windows.Forms.CheckBox();
            this.checkBox5 = new System.Windows.Forms.CheckBox();
            this.checkBox4 = new System.Windows.Forms.CheckBox();
            this.checkBox3 = new System.Windows.Forms.CheckBox();
            this.checkBox2 = new System.Windows.Forms.CheckBox();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.WeeksNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.label11 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.TabMonthly = new System.Windows.Forms.TabPage();
            this.MonthlyOrGroupBox = new System.Windows.Forms.GroupBox();
            this.MonthlyOrCheckBox = new System.Windows.Forms.CheckBox();
            this.WhichComboBox = new System.Windows.Forms.ComboBox();
            this.DaysGroupBox1 = new System.Windows.Forms.GroupBox();
            this.checkBox9 = new System.Windows.Forms.CheckBox();
            this.checkBox10 = new System.Windows.Forms.CheckBox();
            this.checkBox11 = new System.Windows.Forms.CheckBox();
            this.checkBox12 = new System.Windows.Forms.CheckBox();
            this.checkBox13 = new System.Windows.Forms.CheckBox();
            this.checkBox14 = new System.Windows.Forms.CheckBox();
            this.checkBox15 = new System.Windows.Forms.CheckBox();
            this.label5 = new System.Windows.Forms.Label();
            this.MonthlyDaysPicker = new Duplicati.Scheduler.Utility.DayPicker();
            this.TabPageImageList = new System.Windows.Forms.ImageList(this.components);
            this.panel2.SuspendLayout();
            this.PeriodTabControl.SuspendLayout();
            this.TabOnce.SuspendLayout();
            this.TabDaily.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DailyNumericUpDown)).BeginInit();
            this.TabWeekly.SuspendLayout();
            this.WeeklyGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.WeeksNumericUpDown)).BeginInit();
            this.TabMonthly.SuspendLayout();
            this.MonthlyOrGroupBox.SuspendLayout();
            this.DaysGroupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // EnableCheckBox
            // 
            this.EnableCheckBox.AutoSize = true;
            this.EnableCheckBox.Checked = true;
            this.EnableCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.EnableCheckBox.Location = new System.Drawing.Point(3, 6);
            this.EnableCheckBox.Name = "EnableCheckBox";
            this.EnableCheckBox.Size = new System.Drawing.Size(84, 23);
            this.EnableCheckBox.TabIndex = 0;
            this.EnableCheckBox.Text = "Enabled";
            this.EnableCheckBox.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(425, 7);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(95, 19);
            this.label1.TabIndex = 1;
            this.label1.Text = "Begin Time:";
            // 
            // BeginDateTimePicker
            // 
            this.BeginDateTimePicker.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.BeginDateTimePicker.CustomFormat = "hh:mm tt";
            this.BeginDateTimePicker.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.BeginDateTimePicker.Location = new System.Drawing.Point(522, 3);
            this.BeginDateTimePicker.Name = "BeginDateTimePicker";
            this.BeginDateTimePicker.ShowUpDown = true;
            this.BeginDateTimePicker.Size = new System.Drawing.Size(93, 27);
            this.BeginDateTimePicker.TabIndex = 2;
            // 
            // panel2
            // 
            this.panel2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel2.Controls.Add(this.EnableCheckBox);
            this.panel2.Controls.Add(this.label1);
            this.panel2.Controls.Add(this.BeginDateTimePicker);
            this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel2.Location = new System.Drawing.Point(0, 0);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(620, 36);
            this.panel2.TabIndex = 0;
            // 
            // PeriodTabControl
            // 
            this.PeriodTabControl.Controls.Add(this.TabOnce);
            this.PeriodTabControl.Controls.Add(this.TabDaily);
            this.PeriodTabControl.Controls.Add(this.TabWeekly);
            this.PeriodTabControl.Controls.Add(this.TabMonthly);
            this.PeriodTabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PeriodTabControl.ImageList = this.TabPageImageList;
            this.PeriodTabControl.ItemSize = new System.Drawing.Size(70, 30);
            this.PeriodTabControl.Location = new System.Drawing.Point(0, 36);
            this.PeriodTabControl.Name = "PeriodTabControl";
            this.PeriodTabControl.Padding = new System.Drawing.Point(3, 3);
            this.PeriodTabControl.SelectedIndex = 0;
            this.PeriodTabControl.Size = new System.Drawing.Size(620, 344);
            this.PeriodTabControl.TabIndex = 0;
            this.PeriodTabControl.SelectedIndexChanged += new System.EventHandler(this.PeriodTabControl_SelectedIndexChanged);
            // 
            // TabOnce
            // 
            this.TabOnce.BackColor = System.Drawing.Color.Gainsboro;
            this.TabOnce.Controls.Add(this.OnceMonthCalendar);
            this.TabOnce.Controls.Add(this.label2);
            this.TabOnce.ImageIndex = 1;
            this.TabOnce.Location = new System.Drawing.Point(4, 34);
            this.TabOnce.Name = "TabOnce";
            this.TabOnce.Padding = new System.Windows.Forms.Padding(3);
            this.TabOnce.Size = new System.Drawing.Size(612, 306);
            this.TabOnce.TabIndex = 0;
            this.TabOnce.Text = "Once";
            this.TabOnce.UseVisualStyleBackColor = true;
            // 
            // OnceMonthCalendar
            // 
            this.OnceMonthCalendar.Location = new System.Drawing.Point(246, 66);
            this.OnceMonthCalendar.Name = "OnceMonthCalendar";
            this.OnceMonthCalendar.TabIndex = 1;
            this.OnceMonthCalendar.DateChanged += new System.Windows.Forms.DateRangeEventHandler(this.OnceMonthCalendar_DateChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(67, 66);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(167, 19);
            this.label2.TabIndex = 0;
            this.label2.Text = "Run job only once on:";
            // 
            // TabDaily
            // 
            this.TabDaily.BackColor = System.Drawing.Color.Gainsboro;
            this.TabDaily.Controls.Add(this.label4);
            this.TabDaily.Controls.Add(this.DailyNumericUpDown);
            this.TabDaily.Controls.Add(this.label3);
            this.TabDaily.ImageIndex = 0;
            this.TabDaily.Location = new System.Drawing.Point(4, 34);
            this.TabDaily.Name = "TabDaily";
            this.TabDaily.Padding = new System.Windows.Forms.Padding(3);
            this.TabDaily.Size = new System.Drawing.Size(612, 306);
            this.TabDaily.TabIndex = 1;
            this.TabDaily.Text = "Daily";
            this.TabDaily.UseVisualStyleBackColor = true;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(359, 64);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(46, 19);
            this.label4.TabIndex = 5;
            this.label4.Text = "days.";
            // 
            // DailyNumericUpDown
            // 
            this.DailyNumericUpDown.Location = new System.Drawing.Point(270, 62);
            this.DailyNumericUpDown.Name = "DailyNumericUpDown";
            this.DailyNumericUpDown.Size = new System.Drawing.Size(73, 27);
            this.DailyNumericUpDown.TabIndex = 4;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(67, 64);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(197, 19);
            this.label3.TabIndex = 3;
            this.label3.Text = "Set this job to recur every ";
            // 
            // TabWeekly
            // 
            this.TabWeekly.BackColor = System.Drawing.Color.Gainsboro;
            this.TabWeekly.Controls.Add(this.WeeklyGroupBox);
            this.TabWeekly.Controls.Add(this.WeeksNumericUpDown);
            this.TabWeekly.Controls.Add(this.label11);
            this.TabWeekly.Controls.Add(this.label12);
            this.TabWeekly.ImageIndex = 0;
            this.TabWeekly.Location = new System.Drawing.Point(4, 34);
            this.TabWeekly.Name = "TabWeekly";
            this.TabWeekly.Size = new System.Drawing.Size(612, 306);
            this.TabWeekly.TabIndex = 2;
            this.TabWeekly.Text = "Weekly";
            this.TabWeekly.UseVisualStyleBackColor = true;
            // 
            // WeeklyGroupBox
            // 
            this.WeeklyGroupBox.Controls.Add(this.checkBox7);
            this.WeeklyGroupBox.Controls.Add(this.checkBox6);
            this.WeeklyGroupBox.Controls.Add(this.checkBox5);
            this.WeeklyGroupBox.Controls.Add(this.checkBox4);
            this.WeeklyGroupBox.Controls.Add(this.checkBox3);
            this.WeeklyGroupBox.Controls.Add(this.checkBox2);
            this.WeeklyGroupBox.Controls.Add(this.checkBox1);
            this.WeeklyGroupBox.Location = new System.Drawing.Point(140, 113);
            this.WeeklyGroupBox.Name = "WeeklyGroupBox";
            this.WeeklyGroupBox.Size = new System.Drawing.Size(342, 74);
            this.WeeklyGroupBox.TabIndex = 20;
            this.WeeklyGroupBox.TabStop = false;
            // 
            // checkBox7
            // 
            this.checkBox7.AutoSize = true;
            this.checkBox7.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox7.Location = new System.Drawing.Point(294, 20);
            this.checkBox7.Name = "checkBox7";
            this.checkBox7.Size = new System.Drawing.Size(27, 31);
            this.checkBox7.TabIndex = 6;
            this.checkBox7.Text = "Sat";
            this.checkBox7.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox7.UseVisualStyleBackColor = true;
            // 
            // checkBox6
            // 
            this.checkBox6.AutoSize = true;
            this.checkBox6.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox6.Location = new System.Drawing.Point(254, 20);
            this.checkBox6.Name = "checkBox6";
            this.checkBox6.Size = new System.Drawing.Size(22, 31);
            this.checkBox6.TabIndex = 5;
            this.checkBox6.Text = "Fri";
            this.checkBox6.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox6.UseVisualStyleBackColor = true;
            // 
            // checkBox5
            // 
            this.checkBox5.AutoSize = true;
            this.checkBox5.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox5.Location = new System.Drawing.Point(202, 20);
            this.checkBox5.Name = "checkBox5";
            this.checkBox5.Size = new System.Drawing.Size(30, 31);
            this.checkBox5.TabIndex = 4;
            this.checkBox5.Text = "Thu";
            this.checkBox5.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox5.UseVisualStyleBackColor = true;
            // 
            // checkBox4
            // 
            this.checkBox4.AutoSize = true;
            this.checkBox4.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox4.Location = new System.Drawing.Point(152, 20);
            this.checkBox4.Name = "checkBox4";
            this.checkBox4.Size = new System.Drawing.Size(34, 31);
            this.checkBox4.TabIndex = 3;
            this.checkBox4.Text = "Wed";
            this.checkBox4.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox4.UseVisualStyleBackColor = true;
            // 
            // checkBox3
            // 
            this.checkBox3.AutoSize = true;
            this.checkBox3.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox3.Location = new System.Drawing.Point(106, 20);
            this.checkBox3.Name = "checkBox3";
            this.checkBox3.Size = new System.Drawing.Size(30, 31);
            this.checkBox3.TabIndex = 2;
            this.checkBox3.Text = "Tue";
            this.checkBox3.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox3.UseVisualStyleBackColor = true;
            // 
            // checkBox2
            // 
            this.checkBox2.AutoSize = true;
            this.checkBox2.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox2.Checked = true;
            this.checkBox2.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox2.Location = new System.Drawing.Point(57, 20);
            this.checkBox2.Name = "checkBox2";
            this.checkBox2.Size = new System.Drawing.Size(32, 31);
            this.checkBox2.TabIndex = 1;
            this.checkBox2.Text = "Mon";
            this.checkBox2.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox2.UseVisualStyleBackColor = true;
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox1.Location = new System.Drawing.Point(13, 20);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(30, 31);
            this.checkBox1.TabIndex = 0;
            this.checkBox1.Text = "Sun";
            this.checkBox1.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox1.UseVisualStyleBackColor = true;
            // 
            // WeeksNumericUpDown
            // 
            this.WeeksNumericUpDown.Location = new System.Drawing.Point(171, 59);
            this.WeeksNumericUpDown.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.WeeksNumericUpDown.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.WeeksNumericUpDown.Name = "WeeksNumericUpDown";
            this.WeeksNumericUpDown.Size = new System.Drawing.Size(53, 27);
            this.WeeksNumericUpDown.TabIndex = 8;
            this.WeeksNumericUpDown.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(242, 61);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(81, 19);
            this.label11.TabIndex = 7;
            this.label11.Text = "weeks on:";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(67, 61);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(97, 19);
            this.label12.TabIndex = 6;
            this.label12.Text = "Recur every ";
            // 
            // TabMonthly
            // 
            this.TabMonthly.BackColor = System.Drawing.Color.Gainsboro;
            this.TabMonthly.Controls.Add(this.MonthlyOrGroupBox);
            this.TabMonthly.Controls.Add(this.label5);
            this.TabMonthly.Controls.Add(this.MonthlyDaysPicker);
            this.TabMonthly.ImageIndex = 0;
            this.TabMonthly.Location = new System.Drawing.Point(4, 34);
            this.TabMonthly.Name = "TabMonthly";
            this.TabMonthly.Size = new System.Drawing.Size(612, 306);
            this.TabMonthly.TabIndex = 3;
            this.TabMonthly.Text = "Monthly";
            this.TabMonthly.UseVisualStyleBackColor = true;
            // 
            // MonthlyOrGroupBox
            // 
            this.MonthlyOrGroupBox.Controls.Add(this.MonthlyOrCheckBox);
            this.MonthlyOrGroupBox.Controls.Add(this.WhichComboBox);
            this.MonthlyOrGroupBox.Controls.Add(this.DaysGroupBox1);
            this.MonthlyOrGroupBox.Enabled = false;
            this.MonthlyOrGroupBox.Location = new System.Drawing.Point(328, 40);
            this.MonthlyOrGroupBox.Name = "MonthlyOrGroupBox";
            this.MonthlyOrGroupBox.Size = new System.Drawing.Size(273, 228);
            this.MonthlyOrGroupBox.TabIndex = 3;
            this.MonthlyOrGroupBox.TabStop = false;
            this.MonthlyOrGroupBox.EnabledChanged += new System.EventHandler(this.MonthlyOrGroupBox_EnabledChanged);
            // 
            // MonthlyOrCheckBox
            // 
            this.MonthlyOrCheckBox.AutoSize = true;
            this.MonthlyOrCheckBox.Location = new System.Drawing.Point(6, -1);
            this.MonthlyOrCheckBox.Name = "MonthlyOrCheckBox";
            this.MonthlyOrCheckBox.Size = new System.Drawing.Size(149, 23);
            this.MonthlyOrCheckBox.TabIndex = 23;
            this.MonthlyOrCheckBox.Text = "or run job every:";
            this.MonthlyOrCheckBox.UseVisualStyleBackColor = true;
            this.MonthlyOrCheckBox.CheckedChanged += new System.EventHandler(this.MonthlyOrCheckBox_CheckedChanged);
            // 
            // WhichComboBox
            // 
            this.WhichComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.WhichComboBox.FormattingEnabled = true;
            this.WhichComboBox.Items.AddRange(new object[] {
            "First",
            "Second",
            "Third",
            "Last"});
            this.WhichComboBox.Location = new System.Drawing.Point(6, 50);
            this.WhichComboBox.Name = "WhichComboBox";
            this.WhichComboBox.Size = new System.Drawing.Size(111, 27);
            this.WhichComboBox.TabIndex = 22;
            // 
            // DaysGroupBox1
            // 
            this.DaysGroupBox1.Controls.Add(this.checkBox9);
            this.DaysGroupBox1.Controls.Add(this.checkBox10);
            this.DaysGroupBox1.Controls.Add(this.checkBox11);
            this.DaysGroupBox1.Controls.Add(this.checkBox12);
            this.DaysGroupBox1.Controls.Add(this.checkBox13);
            this.DaysGroupBox1.Controls.Add(this.checkBox14);
            this.DaysGroupBox1.Controls.Add(this.checkBox15);
            this.DaysGroupBox1.Location = new System.Drawing.Point(6, 77);
            this.DaysGroupBox1.Name = "DaysGroupBox1";
            this.DaysGroupBox1.Size = new System.Drawing.Size(261, 119);
            this.DaysGroupBox1.TabIndex = 20;
            this.DaysGroupBox1.TabStop = false;
            // 
            // checkBox9
            // 
            this.checkBox9.AutoSize = true;
            this.checkBox9.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox9.Location = new System.Drawing.Point(119, 64);
            this.checkBox9.Name = "checkBox9";
            this.checkBox9.Size = new System.Drawing.Size(27, 31);
            this.checkBox9.TabIndex = 6;
            this.checkBox9.Text = "Sat";
            this.checkBox9.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox9.UseVisualStyleBackColor = true;
            // 
            // checkBox10
            // 
            this.checkBox10.AutoSize = true;
            this.checkBox10.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox10.Location = new System.Drawing.Point(68, 64);
            this.checkBox10.Name = "checkBox10";
            this.checkBox10.Size = new System.Drawing.Size(22, 31);
            this.checkBox10.TabIndex = 5;
            this.checkBox10.Text = "Fri";
            this.checkBox10.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox10.UseVisualStyleBackColor = true;
            // 
            // checkBox11
            // 
            this.checkBox11.AutoSize = true;
            this.checkBox11.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox11.Location = new System.Drawing.Point(17, 64);
            this.checkBox11.Name = "checkBox11";
            this.checkBox11.Size = new System.Drawing.Size(30, 31);
            this.checkBox11.TabIndex = 4;
            this.checkBox11.Text = "Thu";
            this.checkBox11.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox11.UseVisualStyleBackColor = true;
            // 
            // checkBox12
            // 
            this.checkBox12.AutoSize = true;
            this.checkBox12.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox12.Location = new System.Drawing.Point(165, 22);
            this.checkBox12.Name = "checkBox12";
            this.checkBox12.Size = new System.Drawing.Size(34, 31);
            this.checkBox12.TabIndex = 3;
            this.checkBox12.Text = "Wed";
            this.checkBox12.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox12.UseVisualStyleBackColor = true;
            // 
            // checkBox13
            // 
            this.checkBox13.AutoSize = true;
            this.checkBox13.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox13.Location = new System.Drawing.Point(119, 22);
            this.checkBox13.Name = "checkBox13";
            this.checkBox13.Size = new System.Drawing.Size(30, 31);
            this.checkBox13.TabIndex = 2;
            this.checkBox13.Text = "Tue";
            this.checkBox13.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox13.UseVisualStyleBackColor = true;
            // 
            // checkBox14
            // 
            this.checkBox14.AutoSize = true;
            this.checkBox14.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox14.Checked = true;
            this.checkBox14.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox14.Location = new System.Drawing.Point(68, 22);
            this.checkBox14.Name = "checkBox14";
            this.checkBox14.Size = new System.Drawing.Size(32, 31);
            this.checkBox14.TabIndex = 1;
            this.checkBox14.Text = "Mon";
            this.checkBox14.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox14.UseVisualStyleBackColor = true;
            // 
            // checkBox15
            // 
            this.checkBox15.AutoSize = true;
            this.checkBox15.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.checkBox15.Location = new System.Drawing.Point(17, 22);
            this.checkBox15.Name = "checkBox15";
            this.checkBox15.Size = new System.Drawing.Size(30, 31);
            this.checkBox15.TabIndex = 0;
            this.checkBox15.Text = "Sun";
            this.checkBox15.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.checkBox15.UseVisualStyleBackColor = true;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(3, 22);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(239, 19);
            this.label5.TabIndex = 0;
            this.label5.Text = "Run every month on these days:";
            // 
            // MonthlyDaysPicker
            // 
            this.MonthlyDaysPicker.DaysPicked = new int[0];
            this.MonthlyDaysPicker.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.MonthlyDaysPicker.Last = false;
            this.MonthlyDaysPicker.Location = new System.Drawing.Point(3, 44);
            this.MonthlyDaysPicker.MinimumSize = new System.Drawing.Size(200, 200);
            this.MonthlyDaysPicker.Name = "MonthlyDaysPicker";
            this.MonthlyDaysPicker.Size = new System.Drawing.Size(310, 224);
            this.MonthlyDaysPicker.TabIndex = 1;
            // 
            // TabPageImageList
            // 
            this.TabPageImageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("TabPageImageList.ImageStream")));
            this.TabPageImageList.TransparentColor = System.Drawing.Color.Transparent;
            this.TabPageImageList.Images.SetKeyName(0, "checkbox_unchecked.png");
            this.TabPageImageList.Images.SetKeyName(1, "checkbox_checked.png");
            // 
            // TaskEditControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.PeriodTabControl);
            this.Controls.Add(this.panel2);
            this.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "TaskEditControl";
            this.Size = new System.Drawing.Size(620, 380);
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            this.PeriodTabControl.ResumeLayout(false);
            this.TabOnce.ResumeLayout(false);
            this.TabOnce.PerformLayout();
            this.TabDaily.ResumeLayout(false);
            this.TabDaily.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DailyNumericUpDown)).EndInit();
            this.TabWeekly.ResumeLayout(false);
            this.TabWeekly.PerformLayout();
            this.WeeklyGroupBox.ResumeLayout(false);
            this.WeeklyGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.WeeksNumericUpDown)).EndInit();
            this.TabMonthly.ResumeLayout(false);
            this.TabMonthly.PerformLayout();
            this.MonthlyOrGroupBox.ResumeLayout(false);
            this.MonthlyOrGroupBox.PerformLayout();
            this.DaysGroupBox1.ResumeLayout(false);
            this.DaysGroupBox1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DateTimePicker BeginDateTimePicker;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox EnableCheckBox;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.TabControl PeriodTabControl;
        private System.Windows.Forms.TabPage TabOnce;
        private System.Windows.Forms.TabPage TabDaily;
        private System.Windows.Forms.TabPage TabWeekly;
        private System.Windows.Forms.TabPage TabMonthly;
        private System.Windows.Forms.MonthCalendar OnceMonthCalendar;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.NumericUpDown DailyNumericUpDown;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.NumericUpDown WeeksNumericUpDown;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.GroupBox WeeklyGroupBox;
        private System.Windows.Forms.CheckBox checkBox7;
        private System.Windows.Forms.CheckBox checkBox6;
        private System.Windows.Forms.CheckBox checkBox5;
        private System.Windows.Forms.CheckBox checkBox4;
        private System.Windows.Forms.CheckBox checkBox3;
        private System.Windows.Forms.CheckBox checkBox2;
        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.Label label5;
        private Utility.DayPicker MonthlyDaysPicker;
        private System.Windows.Forms.GroupBox MonthlyOrGroupBox;
        private System.Windows.Forms.GroupBox DaysGroupBox1;
        private System.Windows.Forms.CheckBox checkBox9;
        private System.Windows.Forms.CheckBox checkBox10;
        private System.Windows.Forms.CheckBox checkBox11;
        private System.Windows.Forms.CheckBox checkBox12;
        private System.Windows.Forms.CheckBox checkBox13;
        private System.Windows.Forms.CheckBox checkBox14;
        private System.Windows.Forms.CheckBox checkBox15;
        private System.Windows.Forms.ComboBox WhichComboBox;
        private System.Windows.Forms.CheckBox MonthlyOrCheckBox;
        private System.Windows.Forms.ImageList TabPageImageList;

    }
}
