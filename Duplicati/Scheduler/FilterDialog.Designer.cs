namespace Duplicati.Scheduler
{
    partial class FilterDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FilterDialog));
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.toolStripDropDownButton1 = new System.Windows.Forms.ToolStripDropDownButton();
            this.toolStripButton3 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton4 = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripButton = new System.Windows.Forms.ToolStripButton();
            this.BottomToolStrip = new System.Windows.Forms.ToolStrip();
            this.CanButton = new System.Windows.Forms.ToolStripButton();
            this.OKButton = new System.Windows.Forms.ToolStripButton();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.clearAllToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.unclearToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStrip1.SuspendLayout();
            this.BottomToolStrip.SuspendLayout();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // toolStrip1
            // 
            this.toolStrip1.ContextMenuStrip = this.contextMenuStrip1;
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripDropDownButton1,
            this.helpToolStripButton});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(284, 25);
            this.toolStrip1.TabIndex = 0;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // toolStripDropDownButton1
            // 
            this.toolStripDropDownButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.toolStripDropDownButton1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButton3,
            this.toolStripButton4,
            this.toolStripSeparator2,
            this.exitToolStripMenuItem});
            this.toolStripDropDownButton1.Image = ((System.Drawing.Image)(resources.GetObject("toolStripDropDownButton1.Image")));
            this.toolStripDropDownButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripDropDownButton1.Name = "toolStripDropDownButton1";
            this.toolStripDropDownButton1.Size = new System.Drawing.Size(44, 22);
            this.toolStripDropDownButton1.Text = "File  ";
            // 
            // toolStripButton3
            // 
            this.toolStripButton3.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton3.Image")));
            this.toolStripButton3.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton3.Name = "toolStripButton3";
            this.toolStripButton3.Size = new System.Drawing.Size(101, 20);
            this.toolStripButton3.Text = "&Read from file";
            // 
            // toolStripButton4
            // 
            this.toolStripButton4.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton4.Image")));
            this.toolStripButton4.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton4.Name = "toolStripButton4";
            this.toolStripButton4.Size = new System.Drawing.Size(93, 20);
            this.toolStripButton4.Text = "&Save to file...";
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(158, 6);
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(161, 22);
            this.exitToolStripMenuItem.Text = "Exit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.OKButton_Click);
            // 
            // helpToolStripButton
            // 
            this.helpToolStripButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.helpToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.helpToolStripButton.Image = ((System.Drawing.Image)(resources.GetObject("helpToolStripButton.Image")));
            this.helpToolStripButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.helpToolStripButton.Name = "helpToolStripButton";
            this.helpToolStripButton.Size = new System.Drawing.Size(23, 22);
            this.helpToolStripButton.Text = "He&lp";
            this.helpToolStripButton.Click += new System.EventHandler(this.helpToolStripButton_Click);
            // 
            // BottomToolStrip
            // 
            this.BottomToolStrip.ContextMenuStrip = this.contextMenuStrip1;
            this.BottomToolStrip.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.BottomToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.CanButton,
            this.OKButton});
            this.BottomToolStrip.Location = new System.Drawing.Point(0, 227);
            this.BottomToolStrip.MaximumSize = new System.Drawing.Size(0, 35);
            this.BottomToolStrip.MinimumSize = new System.Drawing.Size(0, 35);
            this.BottomToolStrip.Name = "BottomToolStrip";
            this.BottomToolStrip.Size = new System.Drawing.Size(284, 35);
            this.BottomToolStrip.TabIndex = 28;
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
            // OKButton
            // 
            this.OKButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.OKButton.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("OKButton.BackgroundImage")));
            this.OKButton.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.OKButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.OKButton.Image = ((System.Drawing.Image)(resources.GetObject("OKButton.Image")));
            this.OKButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.OKButton.Name = "OKButton";
            this.OKButton.Size = new System.Drawing.Size(63, 32);
            this.OKButton.Text = "      OK      ";
            this.OKButton.Click += new System.EventHandler(this.OKButton_Click);
            // 
            // richTextBox1
            // 
            this.richTextBox1.ContextMenuStrip = this.contextMenuStrip1;
            this.richTextBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.richTextBox1.Location = new System.Drawing.Point(0, 25);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.Size = new System.Drawing.Size(284, 202);
            this.richTextBox1.TabIndex = 29;
            this.richTextBox1.Text = "";
            // 
            // openFileDialog1
            // 
            this.openFileDialog1.FileName = "openFileDialog1";
            this.openFileDialog1.Filter = "Text files|*.txt|All|*.*";
            this.openFileDialog1.ShowReadOnly = true;
            this.openFileDialog1.Title = "Enter file to read";
            // 
            // saveFileDialog1
            // 
            this.saveFileDialog1.Filter = "Text files|*.txt";
            this.saveFileDialog1.Title = "Enter file name to save text";
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.clearAllToolStripMenuItem,
            this.unclearToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(153, 70);
            // 
            // clearAllToolStripMenuItem
            // 
            this.clearAllToolStripMenuItem.Name = "clearAllToolStripMenuItem";
            this.clearAllToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            this.clearAllToolStripMenuItem.Text = "Clear all";
            this.clearAllToolStripMenuItem.Click += new System.EventHandler(this.clearAllToolStripMenuItem_Click);
            // 
            // unclearToolStripMenuItem
            // 
            this.unclearToolStripMenuItem.Enabled = false;
            this.unclearToolStripMenuItem.Name = "unclearToolStripMenuItem";
            this.unclearToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            this.unclearToolStripMenuItem.Text = "Un-clear";
            this.unclearToolStripMenuItem.Click += new System.EventHandler(this.unclearToolStripMenuItem_Click);
            // 
            // FilterDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(284, 262);
            this.Controls.Add(this.richTextBox1);
            this.Controls.Add(this.BottomToolStrip);
            this.Controls.Add(this.toolStrip1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "FilterDialog";
            this.Text = "FilterDialog";
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.BottomToolStrip.ResumeLayout(false);
            this.BottomToolStrip.PerformLayout();
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStrip BottomToolStrip;
        private System.Windows.Forms.ToolStripButton CanButton;
        private System.Windows.Forms.ToolStripButton OKButton;
        private System.Windows.Forms.ToolStripButton helpToolStripButton;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.SaveFileDialog saveFileDialog1;
        private System.Windows.Forms.ToolStripDropDownButton toolStripDropDownButton1;
        private System.Windows.Forms.ToolStripButton toolStripButton3;
        private System.Windows.Forms.ToolStripButton toolStripButton4;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem clearAllToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem unclearToolStripMenuItem;
    }
}