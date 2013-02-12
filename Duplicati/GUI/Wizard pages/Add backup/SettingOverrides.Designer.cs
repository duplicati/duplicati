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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingOverrides));
            this.BaseDataSet = new System.Data.DataSet();
            this.OverrideTable = new System.Data.DataTable();
            this.EnabledDataColumn = new System.Data.DataColumn();
            this.NameDataColumn = new System.Data.DataColumn();
            this.ValueDataColumn = new System.Data.DataColumn();
            this.argumentDataColumn = new System.Data.DataColumn();
            this.dataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.OptionGrid = new Duplicati.GUI.HelperControls.CommandLineOptionGrid();
            ((System.ComponentModel.ISupportInitialize)(this.BaseDataSet)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.OverrideTable)).BeginInit();
            this.SuspendLayout();
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
            // argumentDataColumn
            // 
            this.argumentDataColumn.Caption = "argument";
            this.argumentDataColumn.ColumnName = "argument";
            this.argumentDataColumn.DataType = typeof(object);
            // 
            // dataGridViewTextBoxColumn1
            // 
            this.dataGridViewTextBoxColumn1.DataPropertyName = "argument";
            resources.ApplyResources(this.dataGridViewTextBoxColumn1, "dataGridViewTextBoxColumn1");
            this.dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
            // 
            // OptionGrid
            // 
            resources.ApplyResources(this.OptionGrid, "OptionGrid");
            this.OptionGrid.Name = "OptionGrid";
            // 
            // SettingOverrides
            // 
            this.Controls.Add(this.OptionGrid);
            this.Name = "SettingOverrides";
            resources.ApplyResources(this, "$this");
            ((System.ComponentModel.ISupportInitialize)(this.BaseDataSet)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.OverrideTable)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Data.DataSet BaseDataSet;
        private System.Data.DataTable OverrideTable;
        private System.Data.DataColumn EnabledDataColumn;
        private System.Data.DataColumn NameDataColumn;
        private System.Data.DataColumn ValueDataColumn;
        private System.Data.DataColumn argumentDataColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;
        private HelperControls.CommandLineOptionGrid OptionGrid;
    }
}
