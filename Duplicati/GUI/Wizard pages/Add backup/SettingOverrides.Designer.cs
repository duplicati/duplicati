namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class SettingOverrides
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
            this.OptionsGrid = new System.Windows.Forms.DataGridView();
            this.InfoLabel = new System.Windows.Forms.Label();
            this.BaseDataSet = new System.Data.DataSet();
            this.OverrideTable = new System.Data.DataTable();
            this.EnabledDataColumn = new System.Data.DataColumn();
            this.NameDataColumn = new System.Data.DataColumn();
            this.ValueDataColumn = new System.Data.DataColumn();
            this.enabledDataGridViewCheckBoxColumn = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.nameDataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.valueDataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.dataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.argumentDataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.argumentDataColumn = new System.Data.DataColumn();
            ((System.ComponentModel.ISupportInitialize)(this.OptionsGrid)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.BaseDataSet)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.OverrideTable)).BeginInit();
            this.SuspendLayout();
            // 
            // OptionsGrid
            // 
            this.OptionsGrid.AllowUserToAddRows = false;
            this.OptionsGrid.AllowUserToDeleteRows = false;
            this.OptionsGrid.AutoGenerateColumns = false;
            this.OptionsGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.OptionsGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.enabledDataGridViewCheckBoxColumn,
            this.nameDataGridViewTextBoxColumn,
            this.valueDataGridViewTextBoxColumn,
            this.argumentDataGridViewTextBoxColumn});
            this.OptionsGrid.DataMember = "OverrideTable";
            this.OptionsGrid.DataSource = this.BaseDataSet;
            this.OptionsGrid.Location = new System.Drawing.Point(16, 16);
            this.OptionsGrid.MultiSelect = false;
            this.OptionsGrid.Name = "OptionsGrid";
            this.OptionsGrid.RowHeadersVisible = false;
            this.OptionsGrid.Size = new System.Drawing.Size(472, 144);
            this.OptionsGrid.TabIndex = 0;
            this.OptionsGrid.RowEnter += new System.Windows.Forms.DataGridViewCellEventHandler(this.OptionsGrid_RowEnter);
            this.OptionsGrid.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.OptionsGrid_CellEndEdit);
            // 
            // InfoLabel
            // 
            this.InfoLabel.Location = new System.Drawing.Point(16, 168);
            this.InfoLabel.Name = "InfoLabel";
            this.InfoLabel.Size = new System.Drawing.Size(472, 64);
            this.InfoLabel.TabIndex = 0;
            this.InfoLabel.Text = "Description panel";
            // 
            // BaseDataSet
            // 
            this.BaseDataSet.DataSetName = "BaseDataSet";
            this.BaseDataSet.Tables.AddRange(new System.Data.DataTable[] {
            this.OverrideTable});
            // 
            // OverrideTable
            // 
            this.OverrideTable.Columns.AddRange(new System.Data.DataColumn[] {
            this.EnabledDataColumn,
            this.NameDataColumn,
            this.ValueDataColumn,
            this.argumentDataColumn});
            this.OverrideTable.TableName = "OverrideTable";
            // 
            // EnabledDataColumn
            // 
            this.EnabledDataColumn.Caption = "Enabled";
            this.EnabledDataColumn.ColumnName = "Enabled";
            this.EnabledDataColumn.DataType = typeof(bool);
            // 
            // NameDataColumn
            // 
            this.NameDataColumn.Caption = "Name";
            this.NameDataColumn.ColumnName = "Name";
            // 
            // ValueDataColumn
            // 
            this.ValueDataColumn.Caption = "Value";
            this.ValueDataColumn.ColumnName = "Value";
            // 
            // enabledDataGridViewCheckBoxColumn
            // 
            this.enabledDataGridViewCheckBoxColumn.DataPropertyName = "Enabled";
            this.enabledDataGridViewCheckBoxColumn.HeaderText = "Enabled";
            this.enabledDataGridViewCheckBoxColumn.Name = "enabledDataGridViewCheckBoxColumn";
            this.enabledDataGridViewCheckBoxColumn.Width = 50;
            // 
            // nameDataGridViewTextBoxColumn
            // 
            this.nameDataGridViewTextBoxColumn.DataPropertyName = "Name";
            this.nameDataGridViewTextBoxColumn.HeaderText = "Name";
            this.nameDataGridViewTextBoxColumn.Name = "nameDataGridViewTextBoxColumn";
            this.nameDataGridViewTextBoxColumn.ReadOnly = true;
            this.nameDataGridViewTextBoxColumn.Width = 150;
            // 
            // valueDataGridViewTextBoxColumn
            // 
            this.valueDataGridViewTextBoxColumn.DataPropertyName = "Value";
            this.valueDataGridViewTextBoxColumn.HeaderText = "Value";
            this.valueDataGridViewTextBoxColumn.Name = "valueDataGridViewTextBoxColumn";
            this.valueDataGridViewTextBoxColumn.Width = 240;
            // 
            // dataGridViewTextBoxColumn1
            // 
            this.dataGridViewTextBoxColumn1.DataPropertyName = "argument";
            this.dataGridViewTextBoxColumn1.HeaderText = "argument";
            this.dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
            this.dataGridViewTextBoxColumn1.Visible = false;
            this.dataGridViewTextBoxColumn1.Width = 5;
            // 
            // argumentDataGridViewTextBoxColumn
            // 
            this.argumentDataGridViewTextBoxColumn.DataPropertyName = "argument";
            this.argumentDataGridViewTextBoxColumn.HeaderText = "argument";
            this.argumentDataGridViewTextBoxColumn.Name = "argumentDataGridViewTextBoxColumn";
            this.argumentDataGridViewTextBoxColumn.Visible = false;
            this.argumentDataGridViewTextBoxColumn.Width = 5;
            // 
            // argumentDataColumn
            // 
            this.argumentDataColumn.Caption = "argument";
            this.argumentDataColumn.ColumnName = "argument";
            this.argumentDataColumn.DataType = typeof(object);
            // 
            // SettingOverrides
            // 
            this.Controls.Add(this.InfoLabel);
            this.Controls.Add(this.OptionsGrid);
            this.Name = "SettingOverrides";
            this.Size = new System.Drawing.Size(506, 242);
            ((System.ComponentModel.ISupportInitialize)(this.OptionsGrid)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.BaseDataSet)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.OverrideTable)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView OptionsGrid;
        private System.Windows.Forms.Label InfoLabel;
        private System.Data.DataSet BaseDataSet;
        private System.Data.DataTable OverrideTable;
        private System.Data.DataColumn EnabledDataColumn;
        private System.Data.DataColumn NameDataColumn;
        private System.Data.DataColumn ValueDataColumn;
        private System.Data.DataColumn argumentDataColumn;
        private System.Windows.Forms.DataGridViewCheckBoxColumn enabledDataGridViewCheckBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn nameDataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn valueDataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn argumentDataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;
    }
}
