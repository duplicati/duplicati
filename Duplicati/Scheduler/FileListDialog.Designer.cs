namespace Duplicati.Scheduler
{
    partial class FileListDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FileListDialog));
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
            this.RestoreToolStripButton = new System.Windows.Forms.ToolStripButton();
            this.treeView = new System.Windows.Forms.TreeView();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.ProgressGroupBox = new System.Windows.Forms.GroupBox();
            this.MessageTextBox = new System.Windows.Forms.TextBox();
            this.TimeToolStripLabel = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStrip1.SuspendLayout();
            this.ProgressGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // toolStrip1
            // 
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripLabel1,
            this.RestoreToolStripButton,
            this.toolStripSeparator1,
            this.TimeToolStripLabel});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(624, 25);
            this.toolStrip1.TabIndex = 0;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // toolStripLabel1
            // 
            this.toolStripLabel1.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.toolStripLabel1.Name = "toolStripLabel1";
            this.toolStripLabel1.Size = new System.Drawing.Size(86, 22);
            this.toolStripLabel1.Text = "toolStripLabel1";
            // 
            // RestoreToolStripButton
            // 
            this.RestoreToolStripButton.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("RestoreToolStripButton.BackgroundImage")));
            this.RestoreToolStripButton.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.RestoreToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.RestoreToolStripButton.Image = ((System.Drawing.Image)(resources.GetObject("RestoreToolStripButton.Image")));
            this.RestoreToolStripButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.RestoreToolStripButton.Name = "RestoreToolStripButton";
            this.RestoreToolStripButton.Size = new System.Drawing.Size(56, 22);
            this.RestoreToolStripButton.Text = " Restore ";
            this.RestoreToolStripButton.Click += new System.EventHandler(this.RestoreToolStripButton_Click);
            // 
            // treeView
            // 
            this.treeView.CheckBoxes = true;
            this.treeView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView.Location = new System.Drawing.Point(0, 25);
            this.treeView.Name = "treeView";
            this.treeView.Size = new System.Drawing.Size(624, 417);
            this.treeView.TabIndex = 1;
            this.treeView.AfterCheck += new System.Windows.Forms.TreeViewEventHandler(this.treeView_AfterCheck);
            // 
            // progressBar
            // 
            this.progressBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.progressBar.Location = new System.Drawing.Point(3, 57);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(594, 23);
            this.progressBar.TabIndex = 2;
            // 
            // folderBrowserDialog
            // 
            this.folderBrowserDialog.Description = "Select folder to restore data.";
            this.folderBrowserDialog.RootFolder = System.Environment.SpecialFolder.MyComputer;
            // 
            // ProgressGroupBox
            // 
            this.ProgressGroupBox.Controls.Add(this.MessageTextBox);
            this.ProgressGroupBox.Controls.Add(this.progressBar);
            this.ProgressGroupBox.Location = new System.Drawing.Point(12, 148);
            this.ProgressGroupBox.Name = "ProgressGroupBox";
            this.ProgressGroupBox.Size = new System.Drawing.Size(600, 83);
            this.ProgressGroupBox.TabIndex = 3;
            this.ProgressGroupBox.TabStop = false;
            this.ProgressGroupBox.Text = "Progress";
            // 
            // MessageTextBox
            // 
            this.MessageTextBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.MessageTextBox.Location = new System.Drawing.Point(3, 23);
            this.MessageTextBox.Name = "MessageTextBox";
            this.MessageTextBox.ReadOnly = true;
            this.MessageTextBox.Size = new System.Drawing.Size(594, 27);
            this.MessageTextBox.TabIndex = 0;
            this.MessageTextBox.TabStop = false;
            // 
            // TimeToolStripLabel
            // 
            this.TimeToolStripLabel.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TimeToolStripLabel.Name = "TimeToolStripLabel";
            this.TimeToolStripLabel.Size = new System.Drawing.Size(86, 22);
            this.TimeToolStripLabel.Text = "toolStripLabel2";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // FileListDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(624, 442);
            this.Controls.Add(this.ProgressGroupBox);
            this.Controls.Add(this.treeView);
            this.Controls.Add(this.toolStrip1);
            this.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "FileListDialog";
            this.Text = "File List";
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.ProgressGroupBox.ResumeLayout(false);
            this.ProgressGroupBox.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.TreeView treeView;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.ToolStripLabel toolStripLabel1;
        private System.Windows.Forms.ToolStripButton RestoreToolStripButton;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
        private System.Windows.Forms.GroupBox ProgressGroupBox;
        private System.Windows.Forms.TextBox MessageTextBox;
        private System.Windows.Forms.ToolStripLabel TimeToolStripLabel;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
    }
}