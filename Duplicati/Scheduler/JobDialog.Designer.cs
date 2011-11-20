namespace Duplicati.Scheduler
{
    partial class JobDialog
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
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.Label label8;
            System.Windows.Forms.Label label3;
            System.Windows.Forms.Label label1;
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(JobDialog));
            this.MainTabControl = new System.Windows.Forms.TabControl();
            this.TimeTabPage = new System.Windows.Forms.TabPage();
            this.TaskEditor = new Duplicati.Scheduler.TaskEditControl();
            this.SourceTabPage = new System.Windows.Forms.TabPage();
            this.SourceTabControl = new System.Windows.Forms.TabControl();
            this.TreeViewTabPage = new System.Windows.Forms.TabPage();
            this.folderSelectControl1 = new Duplicati.Scheduler.Utility.FolderSelectControl();
            this.ListViewTabPage = new System.Windows.Forms.TabPage();
            this.SourceListBox = new System.Windows.Forms.ListBox();
            this.FiltersTabPage = new System.Windows.Forms.TabPage();
            this.FilterListBox = new System.Windows.Forms.ListBox();
            this.FilterEditButton = new System.Windows.Forms.Button();
            this.DestTabPage = new System.Windows.Forms.TabPage();
            this.BackEndTableLayoutPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.UIFPanel = new System.Windows.Forms.Panel();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.PasswordMethodComboBox = new System.Windows.Forms.ComboBox();
            this.passwordControl1 = new Duplicati.Scheduler.PasswordControl();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.AdvancedButton = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.FullAfterNRadioButton = new System.Windows.Forms.RadioButton();
            this.FullDaysRadioButton = new System.Windows.Forms.RadioButton();
            this.FullAlwaysRadioButton = new System.Windows.Forms.RadioButton();
            this.FullDaysNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.FullAfterNNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.label4 = new System.Windows.Forms.Label();
            this.MaxAgeNnumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.MaxFullsNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.MaxFullsCheckBox = new System.Windows.Forms.CheckBox();
            this.MaxAgeCheckBox = new System.Windows.Forms.CheckBox();
            this.CleanFullBackupHelptext = new System.Windows.Forms.Label();
            this.SummaryTabPage = new System.Windows.Forms.TabPage();
            this.jobSummary1 = new Duplicati.Scheduler.JobSummary();
            this.TabsImageList = new System.Windows.Forms.ImageList(this.components);
            this.BottomToolStrip = new System.Windows.Forms.ToolStrip();
            this.CanButton = new System.Windows.Forms.ToolStripButton();
            this.NextButton = new System.Windows.Forms.ToolStripButton();
            this.BackButton = new System.Windows.Forms.ToolStripButton();
            this.ExplainToolStripLabel = new System.Windows.Forms.ToolStripLabel();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            label8 = new System.Windows.Forms.Label();
            label3 = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            this.MainTabControl.SuspendLayout();
            this.TimeTabPage.SuspendLayout();
            this.SourceTabPage.SuspendLayout();
            this.SourceTabControl.SuspendLayout();
            this.TreeViewTabPage.SuspendLayout();
            this.ListViewTabPage.SuspendLayout();
            this.FiltersTabPage.SuspendLayout();
            this.DestTabPage.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.FullDaysNumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.FullAfterNNumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxAgeNnumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxFullsNumericUpDown)).BeginInit();
            this.SummaryTabPage.SuspendLayout();
            this.BottomToolStrip.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new System.Drawing.Point(275, 57);
            label8.Name = "label8";
            label8.Size = new System.Drawing.Size(46, 19);
            label8.TabIndex = 35;
            label8.Text = "days.";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(276, 31);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(98, 19);
            label3.TabIndex = 38;
            label3.Text = "full backups.";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(275, 90);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(158, 19);
            label1.TabIndex = 40;
            label1.Text = "incremental backups.";
            // 
            // MainTabControl
            // 
            this.MainTabControl.Controls.Add(this.TimeTabPage);
            this.MainTabControl.Controls.Add(this.SourceTabPage);
            this.MainTabControl.Controls.Add(this.DestTabPage);
            this.MainTabControl.Controls.Add(this.tabPage1);
            this.MainTabControl.Controls.Add(this.tabPage2);
            this.MainTabControl.Controls.Add(this.SummaryTabPage);
            this.MainTabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainTabControl.ImageList = this.TabsImageList;
            this.MainTabControl.Location = new System.Drawing.Point(0, 0);
            this.MainTabControl.Name = "MainTabControl";
            this.MainTabControl.SelectedIndex = 0;
            this.MainTabControl.Size = new System.Drawing.Size(634, 417);
            this.MainTabControl.TabIndex = 24;
            this.MainTabControl.SelectedIndexChanged += new System.EventHandler(this.MainTabControlSelectedIndexChanged);
            // 
            // TimeTabPage
            // 
            this.TimeTabPage.Controls.Add(this.TaskEditor);
            this.TimeTabPage.ImageIndex = 0;
            this.TimeTabPage.Location = new System.Drawing.Point(4, 28);
            this.TimeTabPage.Name = "TimeTabPage";
            this.TimeTabPage.Padding = new System.Windows.Forms.Padding(3);
            this.TimeTabPage.Size = new System.Drawing.Size(626, 385);
            this.TimeTabPage.TabIndex = 0;
            this.TimeTabPage.Text = "Time";
            this.TimeTabPage.ToolTipText = "Enter the time that the backup will start and repeat";
            this.TimeTabPage.UseVisualStyleBackColor = true;
            // 
            // TaskEditor
            // 
            this.TaskEditor.BackColor = System.Drawing.Color.Gainsboro;
            this.TaskEditor.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TaskEditor.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.TaskEditor.Location = new System.Drawing.Point(3, 3);
            this.TaskEditor.Margin = new System.Windows.Forms.Padding(4);
            this.TaskEditor.Name = "TaskEditor";
            this.TaskEditor.Size = new System.Drawing.Size(620, 379);
            this.TaskEditor.TabIndex = 0;
            // 
            // SourceTabPage
            // 
            this.SourceTabPage.BackColor = System.Drawing.Color.Gainsboro;
            this.SourceTabPage.Controls.Add(this.SourceTabControl);
            this.SourceTabPage.ImageIndex = 1;
            this.SourceTabPage.Location = new System.Drawing.Point(4, 28);
            this.SourceTabPage.Name = "SourceTabPage";
            this.SourceTabPage.Padding = new System.Windows.Forms.Padding(3);
            this.SourceTabPage.Size = new System.Drawing.Size(626, 385);
            this.SourceTabPage.TabIndex = 1;
            this.SourceTabPage.Text = "Source";
            this.SourceTabPage.ToolTipText = "Select the directories to backup";
            this.SourceTabPage.UseVisualStyleBackColor = true;
            // 
            // SourceTabControl
            // 
            this.SourceTabControl.Controls.Add(this.TreeViewTabPage);
            this.SourceTabControl.Controls.Add(this.ListViewTabPage);
            this.SourceTabControl.Controls.Add(this.FiltersTabPage);
            this.SourceTabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SourceTabControl.Location = new System.Drawing.Point(3, 3);
            this.SourceTabControl.Name = "SourceTabControl";
            this.SourceTabControl.SelectedIndex = 0;
            this.SourceTabControl.Size = new System.Drawing.Size(186, 68);
            this.SourceTabControl.TabIndex = 18;
            this.SourceTabControl.SelectedIndexChanged += new System.EventHandler(this.SourceTabControl_SelectedIndexChanged);
            // 
            // TreeViewTabPage
            // 
            this.TreeViewTabPage.Controls.Add(this.folderSelectControl1);
            this.TreeViewTabPage.Location = new System.Drawing.Point(4, 28);
            this.TreeViewTabPage.Name = "TreeViewTabPage";
            this.TreeViewTabPage.Padding = new System.Windows.Forms.Padding(3);
            this.TreeViewTabPage.Size = new System.Drawing.Size(178, 36);
            this.TreeViewTabPage.TabIndex = 0;
            this.TreeViewTabPage.Text = "Tree View";
            this.TreeViewTabPage.UseVisualStyleBackColor = true;
            // 
            // folderSelectControl1
            // 
            this.folderSelectControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.folderSelectControl1.Location = new System.Drawing.Point(3, 3);
            this.folderSelectControl1.Margin = new System.Windows.Forms.Padding(4);
            this.folderSelectControl1.Name = "folderSelectControl1";
            this.folderSelectControl1.SelectedFolders = new string[0];
            this.folderSelectControl1.Size = new System.Drawing.Size(172, 36);
            this.folderSelectControl1.State = resources.GetString("folderSelectControl1.State");
            this.folderSelectControl1.TabIndex = 0;
            // 
            // ListViewTabPage
            // 
            this.ListViewTabPage.Controls.Add(this.SourceListBox);
            this.ListViewTabPage.Location = new System.Drawing.Point(4, 28);
            this.ListViewTabPage.Name = "ListViewTabPage";
            this.ListViewTabPage.Padding = new System.Windows.Forms.Padding(3);
            this.ListViewTabPage.Size = new System.Drawing.Size(178, 36);
            this.ListViewTabPage.TabIndex = 1;
            this.ListViewTabPage.Text = "List View";
            this.ListViewTabPage.UseVisualStyleBackColor = true;
            // 
            // SourceListBox
            // 
            this.SourceListBox.BackColor = System.Drawing.Color.Gainsboro;
            this.SourceListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SourceListBox.ItemHeight = 19;
            this.SourceListBox.Location = new System.Drawing.Point(3, 3);
            this.SourceListBox.Name = "SourceListBox";
            this.SourceListBox.Size = new System.Drawing.Size(606, 347);
            this.SourceListBox.Sorted = true;
            this.SourceListBox.TabIndex = 15;
            // 
            // FiltersTabPage
            // 
            this.FiltersTabPage.Controls.Add(this.FilterListBox);
            this.FiltersTabPage.Controls.Add(this.FilterEditButton);
            this.FiltersTabPage.Location = new System.Drawing.Point(4, 28);
            this.FiltersTabPage.Name = "FiltersTabPage";
            this.FiltersTabPage.Size = new System.Drawing.Size(178, 36);
            this.FiltersTabPage.TabIndex = 2;
            this.FiltersTabPage.Text = "Filters";
            this.FiltersTabPage.UseVisualStyleBackColor = true;
            // 
            // FilterListBox
            // 
            this.FilterListBox.FormattingEnabled = true;
            this.FilterListBox.ItemHeight = 19;
            this.FilterListBox.Location = new System.Drawing.Point(3, 3);
            this.FilterListBox.Name = "FilterListBox";
            this.FilterListBox.Size = new System.Drawing.Size(253, 308);
            this.FilterListBox.TabIndex = 1;
            // 
            // FilterEditButton
            // 
            this.FilterEditButton.Location = new System.Drawing.Point(3, 312);
            this.FilterEditButton.Name = "FilterEditButton";
            this.FilterEditButton.Size = new System.Drawing.Size(75, 32);
            this.FilterEditButton.TabIndex = 0;
            this.FilterEditButton.Text = "EDIT";
            this.FilterEditButton.UseVisualStyleBackColor = true;
            this.FilterEditButton.Click += new System.EventHandler(this.FiltersToolStripButton_Click);
            // 
            // DestTabPage
            // 
            this.DestTabPage.BackColor = System.Drawing.Color.Gainsboro;
            this.DestTabPage.Controls.Add(this.tableLayoutPanel1);
            this.DestTabPage.ImageIndex = 2;
            this.DestTabPage.Location = new System.Drawing.Point(4, 28);
            this.DestTabPage.Name = "DestTabPage";
            this.DestTabPage.Padding = new System.Windows.Forms.Padding(3);
            this.DestTabPage.Size = new System.Drawing.Size(626, 385);
            this.DestTabPage.TabIndex = 2;
            this.DestTabPage.Text = "Destination";
            this.DestTabPage.ToolTipText = "Select the backup destination";
            this.DestTabPage.UseVisualStyleBackColor = true;
            // 
            // BackEndTableLayoutPanel
            // 
            this.BackEndTableLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BackEndTableLayoutPanel.Location = new System.Drawing.Point(3, 3);
            this.BackEndTableLayoutPanel.Name = "BackEndTableLayoutPanel";
            this.BackEndTableLayoutPanel.Size = new System.Drawing.Size(614, 37);
            this.BackEndTableLayoutPanel.TabIndex = 0;
            // 
            // UIFPanel
            // 
            this.UIFPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.UIFPanel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.UIFPanel.Location = new System.Drawing.Point(3, 46);
            this.UIFPanel.Name = "UIFPanel";
            this.UIFPanel.Size = new System.Drawing.Size(614, 330);
            this.UIFPanel.TabIndex = 0;
            // 
            // tabPage1
            // 
            this.tabPage1.BackColor = System.Drawing.Color.Gainsboro;
            this.tabPage1.Controls.Add(this.PasswordMethodComboBox);
            this.tabPage1.Controls.Add(this.passwordControl1);
            this.tabPage1.ImageIndex = 3;
            this.tabPage1.Location = new System.Drawing.Point(4, 28);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Size = new System.Drawing.Size(626, 385);
            this.tabPage1.TabIndex = 4;
            this.tabPage1.Text = "Password";
            this.tabPage1.ToolTipText = "Protect the backup with a password";
            // 
            // PasswordMethodComboBox
            // 
            this.PasswordMethodComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.PasswordMethodComboBox.FormattingEnabled = true;
            this.PasswordMethodComboBox.Items.AddRange(new object[] {
            "Do not password protect backup.",
            "Use the password below:",
            "Use the system password (from Settings)."});
            this.PasswordMethodComboBox.Location = new System.Drawing.Point(70, 75);
            this.PasswordMethodComboBox.Name = "PasswordMethodComboBox";
            this.PasswordMethodComboBox.Size = new System.Drawing.Size(341, 27);
            this.PasswordMethodComboBox.TabIndex = 28;
            this.PasswordMethodComboBox.SelectedIndexChanged += new System.EventHandler(this.PasswordMethodComboBox_SelectedIndexChanged);
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
        ((byte)(107)),
        ((byte)(101)),
        ((byte)(40)),
        ((byte)(33)),
        ((byte)(125)),
        ((byte)(211)),
        ((byte)(129)),
        ((byte)(70)),
        ((byte)(137)),
        ((byte)(204)),
        ((byte)(251)),
        ((byte)(223)),
        ((byte)(22)),
        ((byte)(67)),
        ((byte)(157)),
        ((byte)(88)),
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
        ((byte)(171)),
        ((byte)(3)),
        ((byte)(247)),
        ((byte)(127)),
        ((byte)(67)),
        ((byte)(248)),
        ((byte)(57)),
        ((byte)(241)),
        ((byte)(15)),
        ((byte)(157)),
        ((byte)(158)),
        ((byte)(87)),
        ((byte)(135)),
        ((byte)(127)),
        ((byte)(79)),
        ((byte)(73)),
        ((byte)(227)),
        ((byte)(124)),
        ((byte)(189)),
        ((byte)(246)),
        ((byte)(135)),
        ((byte)(9)),
        ((byte)(144)),
        ((byte)(119)),
        ((byte)(7)),
        ((byte)(36)),
        ((byte)(178)),
        ((byte)(93)),
        ((byte)(145)),
        ((byte)(236)),
        ((byte)(142)),
        ((byte)(65)),
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
        ((byte)(85)),
        ((byte)(124)),
        ((byte)(176)),
        ((byte)(116)),
        ((byte)(126)),
        ((byte)(224)),
        ((byte)(78)),
        ((byte)(64)),
        ((byte)(167)),
        ((byte)(137)),
        ((byte)(252)),
        ((byte)(144)),
        ((byte)(171)),
        ((byte)(211)),
        ((byte)(134)),
        ((byte)(156)),
        ((byte)(125)),
        ((byte)(116)),
        ((byte)(70)),
        ((byte)(221)),
        ((byte)(2)),
        ((byte)(65)),
        ((byte)(41)),
        ((byte)(18)),
        ((byte)(176)),
        ((byte)(127)),
        ((byte)(63)),
        ((byte)(206)),
        ((byte)(93)),
        ((byte)(83)),
        ((byte)(246)),
        ((byte)(52)),
        ((byte)(144)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(203)),
        ((byte)(226)),
        ((byte)(77)),
        ((byte)(127)),
        ((byte)(210)),
        ((byte)(213)),
        ((byte)(62)),
        ((byte)(75)),
        ((byte)(136)),
        ((byte)(16)),
        ((byte)(178)),
        ((byte)(148)),
        ((byte)(74)),
        ((byte)(69)),
        ((byte)(83)),
        ((byte)(63)),
        ((byte)(86)),
        ((byte)(185)),
        ((byte)(204)),
        ((byte)(33)),
        ((byte)(26)),
        ((byte)(243)),
        ((byte)(14)),
        ((byte)(139)),
        ((byte)(76)),
        ((byte)(146)),
        ((byte)(136)),
        ((byte)(173)),
        ((byte)(171)),
        ((byte)(249)),
        ((byte)(186)),
        ((byte)(89)),
        ((byte)(216)),
        ((byte)(58)),
        ((byte)(73)),
        ((byte)(111)),
        ((byte)(207)),
        ((byte)(182)),
        ((byte)(195)),
        ((byte)(153)),
        ((byte)(6)),
        ((byte)(41)),
        ((byte)(32)),
        ((byte)(238)),
        ((byte)(123)),
        ((byte)(194)),
        ((byte)(195)),
        ((byte)(130)),
        ((byte)(102)),
        ((byte)(6)),
        ((byte)(223)),
        ((byte)(150)),
        ((byte)(237)),
        ((byte)(193)),
        ((byte)(148)),
        ((byte)(65)),
        ((byte)(135)),
        ((byte)(10)),
        ((byte)(213)),
        ((byte)(211)),
        ((byte)(54)),
        ((byte)(192)),
        ((byte)(144)),
        ((byte)(31)),
        ((byte)(32)),
        ((byte)(74)),
        ((byte)(178)),
        ((byte)(151)),
        ((byte)(189)),
        ((byte)(106)),
        ((byte)(14)),
        ((byte)(19)),
        ((byte)(101)),
        ((byte)(38)),
        ((byte)(249)),
        ((byte)(22)),
        ((byte)(61)),
        ((byte)(209)),
        ((byte)(7)),
        ((byte)(148)),
        ((byte)(245)),
        ((byte)(90)),
        ((byte)(232)),
        ((byte)(2)),
        ((byte)(175)),
        ((byte)(16)),
        ((byte)(123)),
        ((byte)(65)),
        ((byte)(188)),
        ((byte)(228)),
        ((byte)(240)),
        ((byte)(142)),
        ((byte)(28)),
        ((byte)(151)),
        ((byte)(175)),
        ((byte)(226)),
        ((byte)(108)),
        ((byte)(115)),
        ((byte)(245)),
        ((byte)(96)),
        ((byte)(69)),
        ((byte)(93)),
        ((byte)(59)),
        ((byte)(99)),
        ((byte)(97)),
        ((byte)(184)),
        ((byte)(149)),
        ((byte)(225)),
        ((byte)(11)),
        ((byte)(249)),
        ((byte)(68)),
        ((byte)(62)),
        ((byte)(222)),
        ((byte)(92)),
        ((byte)(226)),
        ((byte)(89)),
        ((byte)(177)),
        ((byte)(16)),
        ((byte)(222)),
        ((byte)(6)),
        ((byte)(87)),
        ((byte)(113)),
        ((byte)(205)),
        ((byte)(89)),
        ((byte)(204)),
        ((byte)(5)),
        ((byte)(226)),
        ((byte)(233)),
        ((byte)(203)),
        ((byte)(142)),
        ((byte)(113)),
        ((byte)(65)),
        ((byte)(77)),
        ((byte)(20)),
        ((byte)(109)),
        ((byte)(132)),
        ((byte)(234)),
        ((byte)(91)),
        ((byte)(135)),
        ((byte)(81)),
        ((byte)(232)),
        ((byte)(0)),
        ((byte)(200)),
        ((byte)(21)),
        ((byte)(64)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(0)),
        ((byte)(158)),
        ((byte)(91)),
        ((byte)(198)),
        ((byte)(6)),
        ((byte)(245)),
        ((byte)(23)),
        ((byte)(191)),
        ((byte)(250)),
        ((byte)(164)),
        ((byte)(179)),
        ((byte)(152)),
        ((byte)(23)),
        ((byte)(132)),
        ((byte)(196)),
        ((byte)(198)),
        ((byte)(40)),
        ((byte)(206)),
        ((byte)(33)),
        ((byte)(151)),
        ((byte)(245)),
        ((byte)(177)),
        ((byte)(203)),
        ((byte)(21)),
        ((byte)(210)),
        ((byte)(248)),
        ((byte)(54)),
        ((byte)(244)),
        ((byte)(230)),
        ((byte)(162)),
        ((byte)(11)),
        ((byte)(215)),
        ((byte)(169)),
        ((byte)(205)),
        ((byte)(78)),
        ((byte)(232)),
        ((byte)(245)),
        ((byte)(59)),
        ((byte)(205)),
        ((byte)(71)),
        ((byte)(57)),
        ((byte)(128)),
        ((byte)(120)),
        ((byte)(131)),
        ((byte)(253)),
        ((byte)(6)),
        ((byte)(103)),
        ((byte)(98)),
        ((byte)(128)),
        ((byte)(155)),
        ((byte)(49)),
        ((byte)(23)),
        ((byte)(209)),
        ((byte)(215)),
        ((byte)(146)),
        ((byte)(89)),
        ((byte)(179)),
        ((byte)(75)),
        ((byte)(114)),
        ((byte)(64)),
        ((byte)(54)),
        ((byte)(188)),
        ((byte)(179)),
        ((byte)(105)),
        ((byte)(167))};
            this.passwordControl1.Enabled = false;
            this.passwordControl1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.passwordControl1.Location = new System.Drawing.Point(70, 109);
            this.passwordControl1.Margin = new System.Windows.Forms.Padding(4);
            this.passwordControl1.Name = "passwordControl1";
            this.passwordControl1.Size = new System.Drawing.Size(513, 161);
            this.passwordControl1.TabIndex = 27;
            // 
            // tabPage2
            // 
            this.tabPage2.BackColor = System.Drawing.Color.Gainsboro;
            this.tabPage2.Controls.Add(this.AdvancedButton);
            this.tabPage2.Controls.Add(this.label2);
            this.tabPage2.Controls.Add(this.groupBox1);
            this.tabPage2.Controls.Add(label3);
            this.tabPage2.Controls.Add(this.label4);
            this.tabPage2.Controls.Add(this.MaxAgeNnumericUpDown);
            this.tabPage2.Controls.Add(this.MaxFullsNumericUpDown);
            this.tabPage2.Controls.Add(this.MaxFullsCheckBox);
            this.tabPage2.Controls.Add(this.MaxAgeCheckBox);
            this.tabPage2.Controls.Add(this.CleanFullBackupHelptext);
            this.tabPage2.ImageIndex = 4;
            this.tabPage2.Location = new System.Drawing.Point(4, 28);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Size = new System.Drawing.Size(626, 385);
            this.tabPage2.TabIndex = 5;
            this.tabPage2.Text = "Cleanup";
            // 
            // AdvancedButton
            // 
            this.AdvancedButton.Location = new System.Drawing.Point(23, 321);
            this.AdvancedButton.Name = "AdvancedButton";
            this.AdvancedButton.Size = new System.Drawing.Size(104, 37);
            this.AdvancedButton.TabIndex = 41;
            this.AdvancedButton.Text = "Advanced";
            this.AdvancedButton.UseVisualStyleBackColor = true;
            this.AdvancedButton.Click += new System.EventHandler(this.AdvancedButtonClick);
            // 
            // label2
            // 
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic);
            this.label2.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label2.Location = new System.Drawing.Point(41, 276);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(528, 16);
            this.label2.TabIndex = 40;
            this.label2.Text = "Incremental backups save disk space and run time by only saving files changed sin" +
                "ce the last full backup.";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(label1);
            this.groupBox1.Controls.Add(this.FullAfterNRadioButton);
            this.groupBox1.Controls.Add(this.FullDaysRadioButton);
            this.groupBox1.Controls.Add(this.FullAlwaysRadioButton);
            this.groupBox1.Controls.Add(this.FullDaysNumericUpDown);
            this.groupBox1.Controls.Add(label8);
            this.groupBox1.Controls.Add(this.FullAfterNNumericUpDown);
            this.groupBox1.Location = new System.Drawing.Point(23, 136);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(465, 137);
            this.groupBox1.TabIndex = 39;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Full / Incremental";
            // 
            // FullAfterNRadioButton
            // 
            this.FullAfterNRadioButton.AutoSize = true;
            this.FullAfterNRadioButton.Location = new System.Drawing.Point(21, 88);
            this.FullAfterNRadioButton.Name = "FullAfterNRadioButton";
            this.FullAfterNRadioButton.Size = new System.Drawing.Size(130, 17);
            this.FullAfterNRadioButton.TabIndex = 39;
            this.FullAfterNRadioButton.Text = "Do a full backup after ";
            this.FullAfterNRadioButton.UseVisualStyleBackColor = true;
            this.FullAfterNRadioButton.CheckedChanged += new System.EventHandler(this.CheckedChanged);
            // 
            // FullDaysRadioButton
            // 
            this.FullDaysRadioButton.AutoSize = true;
            this.FullDaysRadioButton.Location = new System.Drawing.Point(21, 55);
            this.FullDaysRadioButton.Name = "FullDaysRadioButton";
            this.FullDaysRadioButton.Size = new System.Drawing.Size(135, 17);
            this.FullDaysRadioButton.TabIndex = 38;
            this.FullDaysRadioButton.Text = "Do a full backup every ";
            this.FullDaysRadioButton.UseVisualStyleBackColor = true;
            this.FullDaysRadioButton.CheckedChanged += new System.EventHandler(this.CheckedChanged);
            // 
            // FullAlwaysRadioButton
            // 
            this.FullAlwaysRadioButton.AutoSize = true;
            this.FullAlwaysRadioButton.Checked = true;
            this.FullAlwaysRadioButton.Location = new System.Drawing.Point(21, 26);
            this.FullAlwaysRadioButton.Name = "FullAlwaysRadioButton";
            this.FullAlwaysRadioButton.Size = new System.Drawing.Size(136, 17);
            this.FullAlwaysRadioButton.TabIndex = 37;
            this.FullAlwaysRadioButton.TabStop = true;
            this.FullAlwaysRadioButton.Text = "Always do full backups.";
            this.FullAlwaysRadioButton.UseVisualStyleBackColor = true;
            this.FullAlwaysRadioButton.CheckedChanged += new System.EventHandler(this.CheckedChanged);
            // 
            // FullDaysNumericUpDown
            // 
            this.FullDaysNumericUpDown.Enabled = false;
            this.FullDaysNumericUpDown.Location = new System.Drawing.Point(211, 55);
            this.FullDaysNumericUpDown.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.FullDaysNumericUpDown.Name = "FullDaysNumericUpDown";
            this.FullDaysNumericUpDown.Size = new System.Drawing.Size(58, 27);
            this.FullDaysNumericUpDown.TabIndex = 34;
            this.FullDaysNumericUpDown.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            // 
            // FullAfterNNumericUpDown
            // 
            this.FullAfterNNumericUpDown.Enabled = false;
            this.FullAfterNNumericUpDown.Location = new System.Drawing.Point(211, 88);
            this.FullAfterNNumericUpDown.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.FullAfterNNumericUpDown.Name = "FullAfterNNumericUpDown";
            this.FullAfterNNumericUpDown.Size = new System.Drawing.Size(58, 27);
            this.FullAfterNNumericUpDown.TabIndex = 36;
            this.FullAfterNNumericUpDown.Value = new decimal(new int[] {
            8,
            0,
            0,
            0});
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(350, 88);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(41, 19);
            this.label4.TabIndex = 17;
            this.label4.Text = "days";
            // 
            // MaxAgeNnumericUpDown
            // 
            this.MaxAgeNnumericUpDown.Location = new System.Drawing.Point(280, 83);
            this.MaxAgeNnumericUpDown.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.MaxAgeNnumericUpDown.Name = "MaxAgeNnumericUpDown";
            this.MaxAgeNnumericUpDown.Size = new System.Drawing.Size(64, 27);
            this.MaxAgeNnumericUpDown.TabIndex = 16;
            this.MaxAgeNnumericUpDown.Value = new decimal(new int[] {
            90,
            0,
            0,
            0});
            // 
            // MaxFullsNumericUpDown
            // 
            this.MaxFullsNumericUpDown.Location = new System.Drawing.Point(208, 28);
            this.MaxFullsNumericUpDown.Minimum = new decimal(new int[] {
            3,
            0,
            0,
            0});
            this.MaxFullsNumericUpDown.Name = "MaxFullsNumericUpDown";
            this.MaxFullsNumericUpDown.Size = new System.Drawing.Size(62, 27);
            this.MaxFullsNumericUpDown.TabIndex = 14;
            this.MaxFullsNumericUpDown.Value = new decimal(new int[] {
            4,
            0,
            0,
            0});
            // 
            // MaxFullsCheckBox
            // 
            this.MaxFullsCheckBox.AutoSize = true;
            this.MaxFullsCheckBox.Checked = true;
            this.MaxFullsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.MaxFullsCheckBox.Location = new System.Drawing.Point(23, 30);
            this.MaxFullsCheckBox.Name = "MaxFullsCheckBox";
            this.MaxFullsCheckBox.Size = new System.Drawing.Size(135, 17);
            this.MaxFullsCheckBox.TabIndex = 13;
            this.MaxFullsCheckBox.Text = "Never keep more than ";
            this.MaxFullsCheckBox.UseVisualStyleBackColor = true;
            this.MaxFullsCheckBox.CheckedChanged += new System.EventHandler(this.CheckedChanged);
            // 
            // MaxAgeCheckBox
            // 
            this.MaxAgeCheckBox.AutoSize = true;
            this.MaxAgeCheckBox.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.MaxAgeCheckBox.Location = new System.Drawing.Point(23, 87);
            this.MaxAgeCheckBox.Name = "MaxAgeCheckBox";
            this.MaxAgeCheckBox.Size = new System.Drawing.Size(179, 17);
            this.MaxAgeCheckBox.TabIndex = 11;
            this.MaxAgeCheckBox.Text = "Never keep backups older than ";
            this.MaxAgeCheckBox.UseVisualStyleBackColor = true;
            this.MaxAgeCheckBox.CheckedChanged += new System.EventHandler(this.CheckedChanged);
            // 
            // CleanFullBackupHelptext
            // 
            this.CleanFullBackupHelptext.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic);
            this.CleanFullBackupHelptext.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.CleanFullBackupHelptext.Location = new System.Drawing.Point(41, 59);
            this.CleanFullBackupHelptext.Name = "CleanFullBackupHelptext";
            this.CleanFullBackupHelptext.Size = new System.Drawing.Size(440, 16);
            this.CleanFullBackupHelptext.TabIndex = 8;
            this.CleanFullBackupHelptext.Text = "To prevent the backups from growing indefinetly, old backups should be deleted re" +
                "gularly";
            // 
            // SummaryTabPage
            // 
            this.SummaryTabPage.BackColor = System.Drawing.Color.Gainsboro;
            this.SummaryTabPage.Controls.Add(this.jobSummary1);
            this.SummaryTabPage.ImageIndex = 5;
            this.SummaryTabPage.Location = new System.Drawing.Point(4, 28);
            this.SummaryTabPage.Name = "SummaryTabPage";
            this.SummaryTabPage.Size = new System.Drawing.Size(626, 385);
            this.SummaryTabPage.TabIndex = 3;
            this.SummaryTabPage.Text = "Summary";
            this.SummaryTabPage.ToolTipText = "Summary of the backup job";
            this.SummaryTabPage.UseVisualStyleBackColor = true;
            // 
            // jobSummary1
            // 
            this.jobSummary1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.jobSummary1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.jobSummary1.Location = new System.Drawing.Point(0, 0);
            this.jobSummary1.Margin = new System.Windows.Forms.Padding(4);
            this.jobSummary1.Name = "jobSummary1";
            this.jobSummary1.Size = new System.Drawing.Size(192, 74);
            this.jobSummary1.TabIndex = 0;
            // 
            // TabsImageList
            // 
            this.TabsImageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("TabsImageList.ImageStream")));
            this.TabsImageList.TransparentColor = System.Drawing.Color.Transparent;
            this.TabsImageList.Images.SetKeyName(0, "school-time-ico.png");
            this.TabsImageList.Images.SetKeyName(1, "Source.png");
            this.TabsImageList.Images.SetKeyName(2, "bedicon.png");
            this.TabsImageList.Images.SetKeyName(3, "password_icon.png");
            this.TabsImageList.Images.SetKeyName(4, "Cleanup.png");
            this.TabsImageList.Images.SetKeyName(5, "clockicon.png");
            this.TabsImageList.Images.SetKeyName(6, "Error.png");
            // 
            // BottomToolStrip
            // 
            this.BottomToolStrip.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.BottomToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.CanButton,
            this.NextButton,
            this.BackButton,
            this.ExplainToolStripLabel});
            this.BottomToolStrip.Location = new System.Drawing.Point(0, 417);
            this.BottomToolStrip.MaximumSize = new System.Drawing.Size(0, 35);
            this.BottomToolStrip.MinimumSize = new System.Drawing.Size(0, 35);
            this.BottomToolStrip.Name = "BottomToolStrip";
            this.BottomToolStrip.Size = new System.Drawing.Size(634, 35);
            this.BottomToolStrip.TabIndex = 27;
            this.BottomToolStrip.Text = "toolStrip3";
            // 
            // CanButton
            // 
            this.CanButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.CanButton.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("CanButton.BackgroundImage")));
            this.CanButton.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.CanButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.CanButton.Image = ((System.Drawing.Image)(resources.GetObject("CanButton.Image")));
            this.CanButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.CanButton.Name = "CanButton";
            this.CanButton.Size = new System.Drawing.Size(68, 32);
            this.CanButton.Text = "  CANCEL  ";
            this.CanButton.Click += new System.EventHandler(this.CanButton_Click);
            // 
            // NextButton
            // 
            this.NextButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.NextButton.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("NextButton.BackgroundImage")));
            this.NextButton.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.NextButton.Image = ((System.Drawing.Image)(resources.GetObject("NextButton.Image")));
            this.NextButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.NextButton.Name = "NextButton";
            this.NextButton.Size = new System.Drawing.Size(62, 32);
            this.NextButton.Text = "  NEXT";
            this.NextButton.TextImageRelation = System.Windows.Forms.TextImageRelation.TextBeforeImage;
            this.NextButton.Click += new System.EventHandler(this.NextButton_Click);
            // 
            // BackButton
            // 
            this.BackButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.BackButton.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("BackButton.BackgroundImage")));
            this.BackButton.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.BackButton.Enabled = false;
            this.BackButton.Image = ((System.Drawing.Image)(resources.GetObject("BackButton.Image")));
            this.BackButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.BackButton.Name = "BackButton";
            this.BackButton.Size = new System.Drawing.Size(63, 32);
            this.BackButton.Text = "BACK  ";
            this.BackButton.Click += new System.EventHandler(this.BackButton_Click);
            // 
            // ExplainToolStripLabel
            // 
            this.ExplainToolStripLabel.Image = ((System.Drawing.Image)(resources.GetObject("ExplainToolStripLabel.Image")));
            this.ExplainToolStripLabel.Name = "ExplainToolStripLabel";
            this.ExplainToolStripLabel.Size = new System.Drawing.Size(102, 32);
            this.ExplainToolStripLabel.Text = "toolStripLabel3";
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 1;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.UIFPanel, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.BackEndTableLayoutPanel, 0, 0);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(3, 3);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 2;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(620, 379);
            this.tableLayoutPanel1.TabIndex = 1;
            // 
            // JobDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(634, 452);
            this.Controls.Add(this.MainTabControl);
            this.Controls.Add(this.BottomToolStrip);
            this.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "JobDialog";
            this.Text = "Edit Job";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.JobDialog_FormClosing);
            this.MainTabControl.ResumeLayout(false);
            this.TimeTabPage.ResumeLayout(false);
            this.SourceTabPage.ResumeLayout(false);
            this.SourceTabControl.ResumeLayout(false);
            this.TreeViewTabPage.ResumeLayout(false);
            this.ListViewTabPage.ResumeLayout(false);
            this.FiltersTabPage.ResumeLayout(false);
            this.DestTabPage.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage2.ResumeLayout(false);
            this.tabPage2.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.FullDaysNumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.FullAfterNNumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxAgeNnumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxFullsNumericUpDown)).EndInit();
            this.SummaryTabPage.ResumeLayout(false);
            this.BottomToolStrip.ResumeLayout(false);
            this.BottomToolStrip.PerformLayout();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TabControl MainTabControl;
        private System.Windows.Forms.TabPage SourceTabPage;
        private System.Windows.Forms.TabPage TimeTabPage;
        private System.Windows.Forms.ListBox SourceListBox;
        private System.Windows.Forms.TabPage DestTabPage;
        private System.Windows.Forms.Panel UIFPanel;
        private System.Windows.Forms.FlowLayoutPanel BackEndTableLayoutPanel;
        private System.Windows.Forms.ToolStrip BottomToolStrip;
        private System.Windows.Forms.ToolStripButton CanButton;
        private System.Windows.Forms.ToolStripButton NextButton;
        private System.Windows.Forms.ToolStripButton BackButton;
        private System.Windows.Forms.TabPage SummaryTabPage;
        private System.Windows.Forms.ToolStripLabel ExplainToolStripLabel;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.CheckBox MaxFullsCheckBox;
        private System.Windows.Forms.CheckBox MaxAgeCheckBox;
        private System.Windows.Forms.Label CleanFullBackupHelptext;
        private System.Windows.Forms.NumericUpDown MaxFullsNumericUpDown;
        private System.Windows.Forms.NumericUpDown MaxAgeNnumericUpDown;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.NumericUpDown FullAfterNNumericUpDown;
        private System.Windows.Forms.NumericUpDown FullDaysNumericUpDown;
        private TaskEditControl TaskEditor;
        private JobSummary jobSummary1;
        private System.Windows.Forms.ImageList TabsImageList;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.RadioButton FullAfterNRadioButton;
        private System.Windows.Forms.RadioButton FullDaysRadioButton;
        private System.Windows.Forms.RadioButton FullAlwaysRadioButton;
        private System.Windows.Forms.Button AdvancedButton;
        private PasswordControl passwordControl1;
        private System.Windows.Forms.TabControl SourceTabControl;
        private System.Windows.Forms.TabPage TreeViewTabPage;
        private System.Windows.Forms.TabPage ListViewTabPage;
        private System.Windows.Forms.TabPage FiltersTabPage;
        private System.Windows.Forms.Button FilterEditButton;
        private System.Windows.Forms.ListBox FilterListBox;
        private System.Windows.Forms.ComboBox PasswordMethodComboBox;
        private Utility.FolderSelectControl folderSelectControl1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
    }
}