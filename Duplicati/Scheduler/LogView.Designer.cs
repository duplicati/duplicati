namespace Duplicati.Scheduler
{
    partial class LogView
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
            System.Windows.Forms.Label addedFilesLabel;
            System.Windows.Forms.Label addedFoldersLabel;
            System.Windows.Forms.Label examinedFilesLabel;
            System.Windows.Forms.Label openedFilesLabel;
            System.Windows.Forms.Label sizeOfModifiedLabel;
            System.Windows.Forms.Label sizeOfAddedLabel;
            System.Windows.Forms.Label sizeOfExaminedLabel;
            System.Windows.Forms.Label unprocessedLabel;
            System.Windows.Forms.Label tooLargeFilesLabel;
            System.Windows.Forms.Label filesWithErrorLabel;
            System.Windows.Forms.Label beginTimeLabel;
            System.Windows.Forms.Label endTimeLabel;
            System.Windows.Forms.Label durationLabel;
            System.Windows.Forms.Label deletedFilesLabel;
            System.Windows.Forms.Label deletedFoldersLabel;
            System.Windows.Forms.Label modifiedFilesLabel;
            System.Windows.Forms.Label label1;
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LogView));
            this.historyBindingSource = new System.Windows.Forms.BindingSource(this.components);
            this.historyDataSet = new Duplicati.Scheduler.Data.HistoryDataSet();
            this.logListBindingSource = new System.Windows.Forms.BindingSource(this.components);
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.logListDataGridView = new System.Windows.Forms.DataGridView();
            this.DateCol = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.TypeCol = new System.Windows.Forms.DataGridViewImageColumn();
            this.MessageCol = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ExMessageCol = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.TypeTextBox = new System.Windows.Forms.TextBox();
            this.StatsBindingSource = new System.Windows.Forms.BindingSource(this.components);
            this.historyStatsBindingSource = new System.Windows.Forms.BindingSource(this.components);
            this.TimeBeginTextBox = new System.Windows.Forms.TextBox();
            this.TimeEndTextBox = new System.Windows.Forms.TextBox();
            this.addedFilesTextBox = new System.Windows.Forms.TextBox();
            this.addedFoldersTextBox = new System.Windows.Forms.TextBox();
            this.examinedFilesTextBox = new System.Windows.Forms.TextBox();
            this.openedFilesTextBox = new System.Windows.Forms.TextBox();
            this.sizeOfModifiedTextBox = new System.Windows.Forms.TextBox();
            this.sizeOfAddedTextBox = new System.Windows.Forms.TextBox();
            this.sizeOfExaminedTextBox = new System.Windows.Forms.TextBox();
            this.unprocessedTextBox = new System.Windows.Forms.TextBox();
            this.tooLargeFilesTextBox = new System.Windows.Forms.TextBox();
            this.filesWithErrorTextBox = new System.Windows.Forms.TextBox();
            this.durationTextBox = new System.Windows.Forms.TextBox();
            this.deletedFilesTextBox = new System.Windows.Forms.TextBox();
            this.deletedFoldersTextBox = new System.Windows.Forms.TextBox();
            this.modifiedFilesTextBox = new System.Windows.Forms.TextBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.historyDataGridView = new System.Windows.Forms.DataGridView();
            this.dataGridViewTextBoxColumn2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.dataGridViewTextBoxColumn3 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.BackupType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.label2 = new System.Windows.Forms.Label();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.copyToClipboardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            addedFilesLabel = new System.Windows.Forms.Label();
            addedFoldersLabel = new System.Windows.Forms.Label();
            examinedFilesLabel = new System.Windows.Forms.Label();
            openedFilesLabel = new System.Windows.Forms.Label();
            sizeOfModifiedLabel = new System.Windows.Forms.Label();
            sizeOfAddedLabel = new System.Windows.Forms.Label();
            sizeOfExaminedLabel = new System.Windows.Forms.Label();
            unprocessedLabel = new System.Windows.Forms.Label();
            tooLargeFilesLabel = new System.Windows.Forms.Label();
            filesWithErrorLabel = new System.Windows.Forms.Label();
            beginTimeLabel = new System.Windows.Forms.Label();
            endTimeLabel = new System.Windows.Forms.Label();
            durationLabel = new System.Windows.Forms.Label();
            deletedFilesLabel = new System.Windows.Forms.Label();
            deletedFoldersLabel = new System.Windows.Forms.Label();
            modifiedFilesLabel = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.historyBindingSource)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.historyDataSet)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.logListBindingSource)).BeginInit();
            this.tableLayoutPanel1.SuspendLayout();
            this.groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.logListDataGridView)).BeginInit();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.StatsBindingSource)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.historyStatsBindingSource)).BeginInit();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.historyDataGridView)).BeginInit();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // addedFilesLabel
            // 
            addedFilesLabel.AutoSize = true;
            addedFilesLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            addedFilesLabel.Location = new System.Drawing.Point(31, 89);
            addedFilesLabel.Name = "addedFilesLabel";
            addedFilesLabel.Size = new System.Drawing.Size(66, 13);
            addedFilesLabel.TabIndex = 47;
            addedFilesLabel.Text = "Added Files:";
            // 
            // addedFoldersLabel
            // 
            addedFoldersLabel.AutoSize = true;
            addedFoldersLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            addedFoldersLabel.Location = new System.Drawing.Point(17, 110);
            addedFoldersLabel.Name = "addedFoldersLabel";
            addedFoldersLabel.Size = new System.Drawing.Size(80, 13);
            addedFoldersLabel.TabIndex = 49;
            addedFoldersLabel.Text = "Added Folders:";
            // 
            // examinedFilesLabel
            // 
            examinedFilesLabel.AutoSize = true;
            examinedFilesLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            examinedFilesLabel.Location = new System.Drawing.Point(16, 131);
            examinedFilesLabel.Name = "examinedFilesLabel";
            examinedFilesLabel.Size = new System.Drawing.Size(81, 13);
            examinedFilesLabel.TabIndex = 51;
            examinedFilesLabel.Text = "Examined Files:";
            // 
            // openedFilesLabel
            // 
            openedFilesLabel.AutoSize = true;
            openedFilesLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            openedFilesLabel.Location = new System.Drawing.Point(24, 152);
            openedFilesLabel.Name = "openedFilesLabel";
            openedFilesLabel.Size = new System.Drawing.Size(73, 13);
            openedFilesLabel.TabIndex = 53;
            openedFilesLabel.Text = "Opened Files:";
            // 
            // sizeOfModifiedLabel
            // 
            sizeOfModifiedLabel.AutoSize = true;
            sizeOfModifiedLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            sizeOfModifiedLabel.Location = new System.Drawing.Point(186, 110);
            sizeOfModifiedLabel.Name = "sizeOfModifiedLabel";
            sizeOfModifiedLabel.Size = new System.Drawing.Size(88, 13);
            sizeOfModifiedLabel.TabIndex = 55;
            sizeOfModifiedLabel.Text = "Size Of Modified:";
            // 
            // sizeOfAddedLabel
            // 
            sizeOfAddedLabel.AutoSize = true;
            sizeOfAddedLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            sizeOfAddedLabel.Location = new System.Drawing.Point(195, 131);
            sizeOfAddedLabel.Name = "sizeOfAddedLabel";
            sizeOfAddedLabel.Size = new System.Drawing.Size(79, 13);
            sizeOfAddedLabel.TabIndex = 57;
            sizeOfAddedLabel.Text = "Size Of Added:";
            // 
            // sizeOfExaminedLabel
            // 
            sizeOfExaminedLabel.AutoSize = true;
            sizeOfExaminedLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            sizeOfExaminedLabel.Location = new System.Drawing.Point(180, 152);
            sizeOfExaminedLabel.Name = "sizeOfExaminedLabel";
            sizeOfExaminedLabel.Size = new System.Drawing.Size(94, 13);
            sizeOfExaminedLabel.TabIndex = 59;
            sizeOfExaminedLabel.Text = "Size Of Examined:";
            // 
            // unprocessedLabel
            // 
            unprocessedLabel.AutoSize = true;
            unprocessedLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            unprocessedLabel.Location = new System.Drawing.Point(24, 173);
            unprocessedLabel.Name = "unprocessedLabel";
            unprocessedLabel.Size = new System.Drawing.Size(73, 13);
            unprocessedLabel.TabIndex = 61;
            unprocessedLabel.Text = "Unprocessed:";
            // 
            // tooLargeFilesLabel
            // 
            tooLargeFilesLabel.AutoSize = true;
            tooLargeFilesLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            tooLargeFilesLabel.Location = new System.Drawing.Point(191, 173);
            tooLargeFilesLabel.Name = "tooLargeFilesLabel";
            tooLargeFilesLabel.Size = new System.Drawing.Size(83, 13);
            tooLargeFilesLabel.TabIndex = 63;
            tooLargeFilesLabel.Text = "Too Large Files:";
            // 
            // filesWithErrorLabel
            // 
            filesWithErrorLabel.AutoSize = true;
            filesWithErrorLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            filesWithErrorLabel.Location = new System.Drawing.Point(13, 192);
            filesWithErrorLabel.Name = "filesWithErrorLabel";
            filesWithErrorLabel.Size = new System.Drawing.Size(84, 13);
            filesWithErrorLabel.TabIndex = 65;
            filesWithErrorLabel.Text = "Files With Error:";
            // 
            // beginTimeLabel
            // 
            beginTimeLabel.AutoSize = true;
            beginTimeLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            beginTimeLabel.Location = new System.Drawing.Point(34, 24);
            beginTimeLabel.Name = "beginTimeLabel";
            beginTimeLabel.Size = new System.Drawing.Size(62, 13);
            beginTimeLabel.TabIndex = 67;
            beginTimeLabel.Text = "Begin Time:";
            // 
            // endTimeLabel
            // 
            endTimeLabel.AutoSize = true;
            endTimeLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            endTimeLabel.Location = new System.Drawing.Point(42, 47);
            endTimeLabel.Name = "endTimeLabel";
            endTimeLabel.Size = new System.Drawing.Size(54, 13);
            endTimeLabel.TabIndex = 69;
            endTimeLabel.Text = "End Time:";
            // 
            // durationLabel
            // 
            durationLabel.AutoSize = true;
            durationLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            durationLabel.Location = new System.Drawing.Point(44, 67);
            durationLabel.Name = "durationLabel";
            durationLabel.Size = new System.Drawing.Size(52, 13);
            durationLabel.TabIndex = 71;
            durationLabel.Text = "Duration:";
            // 
            // deletedFilesLabel
            // 
            deletedFilesLabel.AutoSize = true;
            deletedFilesLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            deletedFilesLabel.Location = new System.Drawing.Point(202, 47);
            deletedFilesLabel.Name = "deletedFilesLabel";
            deletedFilesLabel.Size = new System.Drawing.Size(72, 13);
            deletedFilesLabel.TabIndex = 73;
            deletedFilesLabel.Text = "Deleted Files:";
            // 
            // deletedFoldersLabel
            // 
            deletedFoldersLabel.AutoSize = true;
            deletedFoldersLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            deletedFoldersLabel.Location = new System.Drawing.Point(188, 68);
            deletedFoldersLabel.Name = "deletedFoldersLabel";
            deletedFoldersLabel.Size = new System.Drawing.Size(86, 13);
            deletedFoldersLabel.TabIndex = 75;
            deletedFoldersLabel.Text = "Deleted Folders:";
            // 
            // modifiedFilesLabel
            // 
            modifiedFilesLabel.AutoSize = true;
            modifiedFilesLabel.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            modifiedFilesLabel.Location = new System.Drawing.Point(199, 89);
            modifiedFilesLabel.Name = "modifiedFilesLabel";
            modifiedFilesLabel.Size = new System.Drawing.Size(75, 13);
            modifiedFilesLabel.TabIndex = 77;
            modifiedFilesLabel.Text = "Modified Files:";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            label1.Location = new System.Drawing.Point(239, 24);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(35, 13);
            label1.TabIndex = 81;
            label1.Text = "Type:";
            // 
            // historyBindingSource
            // 
            this.historyBindingSource.AllowNew = false;
            this.historyBindingSource.DataMember = "History";
            this.historyBindingSource.DataSource = this.historyDataSet;
            this.historyBindingSource.CurrentChanged += new System.EventHandler(this.historyBindingSource_CurrentChanged);
            // 
            // historyDataSet
            // 
            this.historyDataSet.DataSetName = "HistoryDataSet";
            this.historyDataSet.SchemaSerializationMode = System.Data.SchemaSerializationMode.IncludeSchema;
            // 
            // logListBindingSource
            // 
            this.logListBindingSource.DataSource = typeof(Duplicati.Library.Logging.AppendLog.LogEntry);
            this.logListBindingSource.Sort = "DATE ASC";
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.Controls.Add(this.groupBox3, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.groupBox2, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.groupBox1, 0, 0);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 2;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(729, 442);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // groupBox3
            // 
            this.tableLayoutPanel1.SetColumnSpan(this.groupBox3, 2);
            this.groupBox3.Controls.Add(this.label2);
            this.groupBox3.Controls.Add(this.logListDataGridView);
            this.groupBox3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox3.Location = new System.Drawing.Point(3, 224);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(723, 215);
            this.groupBox3.TabIndex = 2;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Log";
            // 
            // logListDataGridView
            // 
            this.logListDataGridView.AllowUserToAddRows = false;
            this.logListDataGridView.AllowUserToDeleteRows = false;
            this.logListDataGridView.AutoGenerateColumns = false;
            this.logListDataGridView.BackgroundColor = System.Drawing.SystemColors.Control;
            this.logListDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.logListDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.DateCol,
            this.TypeCol,
            this.MessageCol,
            this.ExMessageCol});
            this.logListDataGridView.ContextMenuStrip = this.contextMenuStrip1;
            this.logListDataGridView.DataSource = this.logListBindingSource;
            this.logListDataGridView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logListDataGridView.Location = new System.Drawing.Point(3, 23);
            this.logListDataGridView.Name = "logListDataGridView";
            this.logListDataGridView.ReadOnly = true;
            this.logListDataGridView.Size = new System.Drawing.Size(717, 189);
            this.logListDataGridView.TabIndex = 0;
            this.logListDataGridView.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.logListDataGridView_CellDoubleClick);
            this.logListDataGridView.CellFormatting += new System.Windows.Forms.DataGridViewCellFormattingEventHandler(this.logListDataGridView_CellFormatting);
            // 
            // DateCol
            // 
            this.DateCol.DataPropertyName = "Date";
            dataGridViewCellStyle1.Format = "T";
            this.DateCol.DefaultCellStyle = dataGridViewCellStyle1;
            this.DateCol.HeaderText = "";
            this.DateCol.Name = "DateCol";
            this.DateCol.ReadOnly = true;
            // 
            // TypeCol
            // 
            this.TypeCol.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.TypeCol.DataPropertyName = "Type";
            this.TypeCol.HeaderText = "";
            this.TypeCol.Name = "TypeCol";
            this.TypeCol.ReadOnly = true;
            this.TypeCol.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.TypeCol.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.TypeCol.Width = 19;
            // 
            // MessageCol
            // 
            this.MessageCol.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.MessageCol.DataPropertyName = "Message";
            this.MessageCol.HeaderText = "Message";
            this.MessageCol.Name = "MessageCol";
            this.MessageCol.ReadOnly = true;
            this.MessageCol.Width = 93;
            // 
            // ExMessageCol
            // 
            this.ExMessageCol.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.ExMessageCol.DataPropertyName = "ExMessage";
            this.ExMessageCol.HeaderText = "ExMessage";
            this.ExMessageCol.Name = "ExMessageCol";
            this.ExMessageCol.ReadOnly = true;
            this.ExMessageCol.Width = 110;
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.TypeTextBox);
            this.groupBox2.Controls.Add(label1);
            this.groupBox2.Controls.Add(this.TimeBeginTextBox);
            this.groupBox2.Controls.Add(this.TimeEndTextBox);
            this.groupBox2.Controls.Add(addedFilesLabel);
            this.groupBox2.Controls.Add(this.addedFilesTextBox);
            this.groupBox2.Controls.Add(addedFoldersLabel);
            this.groupBox2.Controls.Add(this.addedFoldersTextBox);
            this.groupBox2.Controls.Add(examinedFilesLabel);
            this.groupBox2.Controls.Add(this.examinedFilesTextBox);
            this.groupBox2.Controls.Add(openedFilesLabel);
            this.groupBox2.Controls.Add(this.openedFilesTextBox);
            this.groupBox2.Controls.Add(sizeOfModifiedLabel);
            this.groupBox2.Controls.Add(this.sizeOfModifiedTextBox);
            this.groupBox2.Controls.Add(sizeOfAddedLabel);
            this.groupBox2.Controls.Add(this.sizeOfAddedTextBox);
            this.groupBox2.Controls.Add(sizeOfExaminedLabel);
            this.groupBox2.Controls.Add(this.sizeOfExaminedTextBox);
            this.groupBox2.Controls.Add(unprocessedLabel);
            this.groupBox2.Controls.Add(this.unprocessedTextBox);
            this.groupBox2.Controls.Add(tooLargeFilesLabel);
            this.groupBox2.Controls.Add(this.tooLargeFilesTextBox);
            this.groupBox2.Controls.Add(filesWithErrorLabel);
            this.groupBox2.Controls.Add(this.filesWithErrorTextBox);
            this.groupBox2.Controls.Add(beginTimeLabel);
            this.groupBox2.Controls.Add(endTimeLabel);
            this.groupBox2.Controls.Add(durationLabel);
            this.groupBox2.Controls.Add(this.durationTextBox);
            this.groupBox2.Controls.Add(deletedFilesLabel);
            this.groupBox2.Controls.Add(this.deletedFilesTextBox);
            this.groupBox2.Controls.Add(deletedFoldersLabel);
            this.groupBox2.Controls.Add(this.deletedFoldersTextBox);
            this.groupBox2.Controls.Add(modifiedFilesLabel);
            this.groupBox2.Controls.Add(this.modifiedFilesTextBox);
            this.groupBox2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox2.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.groupBox2.Location = new System.Drawing.Point(367, 3);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(359, 215);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Statistics";
            // 
            // TypeTextBox
            // 
            this.TypeTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "BackupType", true));
            this.TypeTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.TypeTextBox.Location = new System.Drawing.Point(280, 21);
            this.TypeTextBox.Name = "TypeTextBox";
            this.TypeTextBox.ReadOnly = true;
            this.TypeTextBox.Size = new System.Drawing.Size(68, 21);
            this.TypeTextBox.TabIndex = 82;
            this.TypeTextBox.TabStop = false;
            // 
            // StatsBindingSource
            // 
            this.StatsBindingSource.DataSource = this.historyStatsBindingSource;
            // 
            // historyStatsBindingSource
            // 
            this.historyStatsBindingSource.DataMember = "History_Stats";
            this.historyStatsBindingSource.DataSource = this.historyBindingSource;
            // 
            // TimeBeginTextBox
            // 
            this.TimeBeginTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "BeginTime", true));
            this.TimeBeginTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.TimeBeginTextBox.Location = new System.Drawing.Point(103, 21);
            this.TimeBeginTextBox.Name = "TimeBeginTextBox";
            this.TimeBeginTextBox.ReadOnly = true;
            this.TimeBeginTextBox.Size = new System.Drawing.Size(68, 21);
            this.TimeBeginTextBox.TabIndex = 80;
            this.TimeBeginTextBox.TabStop = false;
            // 
            // TimeEndTextBox
            // 
            this.TimeEndTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "EndTime", true));
            this.TimeEndTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.TimeEndTextBox.Location = new System.Drawing.Point(103, 42);
            this.TimeEndTextBox.Name = "TimeEndTextBox";
            this.TimeEndTextBox.ReadOnly = true;
            this.TimeEndTextBox.Size = new System.Drawing.Size(68, 21);
            this.TimeEndTextBox.TabIndex = 79;
            this.TimeEndTextBox.TabStop = false;
            // 
            // addedFilesTextBox
            // 
            this.addedFilesTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "AddedFiles", true));
            this.addedFilesTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.addedFilesTextBox.Location = new System.Drawing.Point(103, 84);
            this.addedFilesTextBox.Name = "addedFilesTextBox";
            this.addedFilesTextBox.ReadOnly = true;
            this.addedFilesTextBox.Size = new System.Drawing.Size(68, 21);
            this.addedFilesTextBox.TabIndex = 48;
            this.addedFilesTextBox.TabStop = false;
            // 
            // addedFoldersTextBox
            // 
            this.addedFoldersTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "AddedFolders", true));
            this.addedFoldersTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.addedFoldersTextBox.Location = new System.Drawing.Point(103, 105);
            this.addedFoldersTextBox.Name = "addedFoldersTextBox";
            this.addedFoldersTextBox.ReadOnly = true;
            this.addedFoldersTextBox.Size = new System.Drawing.Size(68, 21);
            this.addedFoldersTextBox.TabIndex = 50;
            this.addedFoldersTextBox.TabStop = false;
            // 
            // examinedFilesTextBox
            // 
            this.examinedFilesTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "ExaminedFiles", true));
            this.examinedFilesTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.examinedFilesTextBox.Location = new System.Drawing.Point(103, 126);
            this.examinedFilesTextBox.Name = "examinedFilesTextBox";
            this.examinedFilesTextBox.ReadOnly = true;
            this.examinedFilesTextBox.Size = new System.Drawing.Size(68, 21);
            this.examinedFilesTextBox.TabIndex = 52;
            this.examinedFilesTextBox.TabStop = false;
            // 
            // openedFilesTextBox
            // 
            this.openedFilesTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "OpenedFiles", true));
            this.openedFilesTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.openedFilesTextBox.Location = new System.Drawing.Point(103, 147);
            this.openedFilesTextBox.Name = "openedFilesTextBox";
            this.openedFilesTextBox.ReadOnly = true;
            this.openedFilesTextBox.Size = new System.Drawing.Size(68, 21);
            this.openedFilesTextBox.TabIndex = 54;
            this.openedFilesTextBox.TabStop = false;
            // 
            // sizeOfModifiedTextBox
            // 
            this.sizeOfModifiedTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "SizeOfModified", true));
            this.sizeOfModifiedTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.sizeOfModifiedTextBox.Location = new System.Drawing.Point(280, 107);
            this.sizeOfModifiedTextBox.Name = "sizeOfModifiedTextBox";
            this.sizeOfModifiedTextBox.ReadOnly = true;
            this.sizeOfModifiedTextBox.Size = new System.Drawing.Size(68, 21);
            this.sizeOfModifiedTextBox.TabIndex = 56;
            this.sizeOfModifiedTextBox.TabStop = false;
            // 
            // sizeOfAddedTextBox
            // 
            this.sizeOfAddedTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "SizeOfAdded", true));
            this.sizeOfAddedTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.sizeOfAddedTextBox.Location = new System.Drawing.Point(280, 128);
            this.sizeOfAddedTextBox.Name = "sizeOfAddedTextBox";
            this.sizeOfAddedTextBox.ReadOnly = true;
            this.sizeOfAddedTextBox.Size = new System.Drawing.Size(68, 21);
            this.sizeOfAddedTextBox.TabIndex = 58;
            this.sizeOfAddedTextBox.TabStop = false;
            // 
            // sizeOfExaminedTextBox
            // 
            this.sizeOfExaminedTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "SizeOfExamined", true));
            this.sizeOfExaminedTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.sizeOfExaminedTextBox.Location = new System.Drawing.Point(280, 149);
            this.sizeOfExaminedTextBox.Name = "sizeOfExaminedTextBox";
            this.sizeOfExaminedTextBox.ReadOnly = true;
            this.sizeOfExaminedTextBox.Size = new System.Drawing.Size(68, 21);
            this.sizeOfExaminedTextBox.TabIndex = 60;
            this.sizeOfExaminedTextBox.TabStop = false;
            // 
            // unprocessedTextBox
            // 
            this.unprocessedTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "Unprocessed", true));
            this.unprocessedTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.unprocessedTextBox.Location = new System.Drawing.Point(103, 168);
            this.unprocessedTextBox.Name = "unprocessedTextBox";
            this.unprocessedTextBox.ReadOnly = true;
            this.unprocessedTextBox.Size = new System.Drawing.Size(68, 21);
            this.unprocessedTextBox.TabIndex = 62;
            this.unprocessedTextBox.TabStop = false;
            // 
            // tooLargeFilesTextBox
            // 
            this.tooLargeFilesTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "TooLargeFiles", true));
            this.tooLargeFilesTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.tooLargeFilesTextBox.Location = new System.Drawing.Point(280, 170);
            this.tooLargeFilesTextBox.Name = "tooLargeFilesTextBox";
            this.tooLargeFilesTextBox.ReadOnly = true;
            this.tooLargeFilesTextBox.Size = new System.Drawing.Size(68, 21);
            this.tooLargeFilesTextBox.TabIndex = 64;
            this.tooLargeFilesTextBox.TabStop = false;
            // 
            // filesWithErrorTextBox
            // 
            this.filesWithErrorTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "FilesWithError", true));
            this.filesWithErrorTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.filesWithErrorTextBox.Location = new System.Drawing.Point(103, 189);
            this.filesWithErrorTextBox.Name = "filesWithErrorTextBox";
            this.filesWithErrorTextBox.ReadOnly = true;
            this.filesWithErrorTextBox.Size = new System.Drawing.Size(68, 21);
            this.filesWithErrorTextBox.TabIndex = 66;
            this.filesWithErrorTextBox.TabStop = false;
            // 
            // durationTextBox
            // 
            this.durationTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "Duration", true));
            this.durationTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.durationTextBox.Location = new System.Drawing.Point(103, 62);
            this.durationTextBox.Name = "durationTextBox";
            this.durationTextBox.ReadOnly = true;
            this.durationTextBox.Size = new System.Drawing.Size(68, 21);
            this.durationTextBox.TabIndex = 72;
            this.durationTextBox.TabStop = false;
            // 
            // deletedFilesTextBox
            // 
            this.deletedFilesTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "DeletedFiles", true));
            this.deletedFilesTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.deletedFilesTextBox.Location = new System.Drawing.Point(280, 44);
            this.deletedFilesTextBox.Name = "deletedFilesTextBox";
            this.deletedFilesTextBox.ReadOnly = true;
            this.deletedFilesTextBox.Size = new System.Drawing.Size(68, 21);
            this.deletedFilesTextBox.TabIndex = 74;
            this.deletedFilesTextBox.TabStop = false;
            // 
            // deletedFoldersTextBox
            // 
            this.deletedFoldersTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "DeletedFolders", true));
            this.deletedFoldersTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.deletedFoldersTextBox.Location = new System.Drawing.Point(280, 65);
            this.deletedFoldersTextBox.Name = "deletedFoldersTextBox";
            this.deletedFoldersTextBox.ReadOnly = true;
            this.deletedFoldersTextBox.Size = new System.Drawing.Size(68, 21);
            this.deletedFoldersTextBox.TabIndex = 76;
            this.deletedFoldersTextBox.TabStop = false;
            // 
            // modifiedFilesTextBox
            // 
            this.modifiedFilesTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", this.StatsBindingSource, "ModifiedFiles", true));
            this.modifiedFilesTextBox.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.modifiedFilesTextBox.Location = new System.Drawing.Point(280, 86);
            this.modifiedFilesTextBox.Name = "modifiedFilesTextBox";
            this.modifiedFilesTextBox.ReadOnly = true;
            this.modifiedFilesTextBox.Size = new System.Drawing.Size(68, 21);
            this.modifiedFilesTextBox.TabIndex = 78;
            this.modifiedFilesTextBox.TabStop = false;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.historyDataGridView);
            this.groupBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox1.Location = new System.Drawing.Point(3, 3);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(358, 215);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Select event:";
            // 
            // historyDataGridView
            // 
            this.historyDataGridView.AutoGenerateColumns = false;
            this.historyDataGridView.BackgroundColor = System.Drawing.SystemColors.Control;
            this.historyDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.historyDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.dataGridViewTextBoxColumn2,
            this.dataGridViewTextBoxColumn3,
            this.BackupType});
            this.historyDataGridView.DataSource = this.historyBindingSource;
            this.historyDataGridView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.historyDataGridView.Location = new System.Drawing.Point(3, 23);
            this.historyDataGridView.Name = "historyDataGridView";
            this.historyDataGridView.Size = new System.Drawing.Size(352, 189);
            this.historyDataGridView.TabIndex = 0;
            // 
            // dataGridViewTextBoxColumn2
            // 
            this.dataGridViewTextBoxColumn2.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.dataGridViewTextBoxColumn2.DataPropertyName = "ActionDate";
            dataGridViewCellStyle2.Format = "G";
            dataGridViewCellStyle2.NullValue = null;
            this.dataGridViewTextBoxColumn2.DefaultCellStyle = dataGridViewCellStyle2;
            this.dataGridViewTextBoxColumn2.HeaderText = "Date";
            this.dataGridViewTextBoxColumn2.Name = "dataGridViewTextBoxColumn2";
            // 
            // dataGridViewTextBoxColumn3
            // 
            this.dataGridViewTextBoxColumn3.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.dataGridViewTextBoxColumn3.DataPropertyName = "Action";
            this.dataGridViewTextBoxColumn3.HeaderText = "Action";
            this.dataGridViewTextBoxColumn3.Name = "dataGridViewTextBoxColumn3";
            this.dataGridViewTextBoxColumn3.Width = 79;
            // 
            // BackupType
            // 
            this.BackupType.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.BackupType.DataPropertyName = "BackupType";
            this.BackupType.HeaderText = "";
            this.BackupType.Name = "BackupType";
            this.BackupType.Width = 19;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(157, 5);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(173, 13);
            this.label2.TabIndex = 1;
            this.label2.Text = "*Double-click entry for better view";
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.copyToClipboardToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(170, 48);
            // 
            // copyToClipboardToolStripMenuItem
            // 
            this.copyToClipboardToolStripMenuItem.Name = "copyToClipboardToolStripMenuItem";
            this.copyToClipboardToolStripMenuItem.Size = new System.Drawing.Size(169, 22);
            this.copyToClipboardToolStripMenuItem.Text = "Copy to clipboard";
            this.copyToClipboardToolStripMenuItem.Click += new System.EventHandler(this.copyToClipboardToolStripMenuItem_Click);
            // 
            // LogView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(729, 442);
            this.Controls.Add(this.tableLayoutPanel1);
            this.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "LogView";
            this.Text = "LogView";
            ((System.ComponentModel.ISupportInitialize)(this.historyBindingSource)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.historyDataSet)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.logListBindingSource)).EndInit();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.logListDataGridView)).EndInit();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.StatsBindingSource)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.historyStatsBindingSource)).EndInit();
            this.groupBox1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.historyDataGridView)).EndInit();
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.BindingSource logListBindingSource;
        private System.Windows.Forms.BindingSource historyBindingSource;
        private Duplicati.Scheduler.Data.HistoryDataSet historyDataSet;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.DataGridView historyDataGridView;
        private System.Windows.Forms.DataGridViewTextBoxColumn dataGridViewTextBoxColumn2;
        private System.Windows.Forms.DataGridViewTextBoxColumn dataGridViewTextBoxColumn3;
        private System.Windows.Forms.DataGridViewTextBoxColumn BackupType;
        private System.Windows.Forms.DataGridView logListDataGridView;
        private System.Windows.Forms.TextBox addedFilesTextBox;
        private System.Windows.Forms.TextBox addedFoldersTextBox;
        private System.Windows.Forms.TextBox examinedFilesTextBox;
        private System.Windows.Forms.TextBox openedFilesTextBox;
        private System.Windows.Forms.TextBox sizeOfModifiedTextBox;
        private System.Windows.Forms.TextBox sizeOfAddedTextBox;
        private System.Windows.Forms.TextBox sizeOfExaminedTextBox;
        private System.Windows.Forms.TextBox unprocessedTextBox;
        private System.Windows.Forms.TextBox tooLargeFilesTextBox;
        private System.Windows.Forms.TextBox filesWithErrorTextBox;
        private System.Windows.Forms.TextBox durationTextBox;
        private System.Windows.Forms.TextBox deletedFilesTextBox;
        private System.Windows.Forms.TextBox deletedFoldersTextBox;
        private System.Windows.Forms.TextBox modifiedFilesTextBox;
        private System.Windows.Forms.TextBox TimeBeginTextBox;
        private System.Windows.Forms.TextBox TimeEndTextBox;
        private System.Windows.Forms.BindingSource StatsBindingSource;
        private System.Windows.Forms.BindingSource historyStatsBindingSource;
        private System.Windows.Forms.TextBox TypeTextBox;
        private System.Windows.Forms.DataGridViewTextBoxColumn DateCol;
        private System.Windows.Forms.DataGridViewImageColumn TypeCol;
        private System.Windows.Forms.DataGridViewTextBoxColumn MessageCol;
        private System.Windows.Forms.DataGridViewTextBoxColumn ExMessageCol;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem copyToClipboardToolStripMenuItem;
    }
}