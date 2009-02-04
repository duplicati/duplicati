namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class EditFilters
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
            this.filterEditor1 = new Duplicati.GUI.HelperControls.FilterEditor();
            this.SuspendLayout();
            // 
            // filterEditor1
            // 
            this.filterEditor1.BasePath = "";
            this.filterEditor1.Filter = "";
            this.filterEditor1.Location = new System.Drawing.Point(24, 16);
            this.filterEditor1.Name = "filterEditor1";
            this.filterEditor1.Size = new System.Drawing.Size(464, 208);
            this.filterEditor1.TabIndex = 0;
            // 
            // EditFilters
            // 
            this.Controls.Add(this.filterEditor1);
            this.Name = "EditFilters";
            this.Size = new System.Drawing.Size(506, 242);
            this.ResumeLayout(false);

        }

        #endregion

        private Duplicati.GUI.HelperControls.FilterEditor filterEditor1;


    }
}
