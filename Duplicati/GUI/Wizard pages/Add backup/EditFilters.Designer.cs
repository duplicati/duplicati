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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(EditFilters));
            this.filterEditor1 = new Duplicati.GUI.HelperControls.FilterEditor();
            this.SuspendLayout();
            // 
            // filterEditor1
            // 
            resources.ApplyResources(this.filterEditor1, "filterEditor1");
            this.filterEditor1.Name = "filterEditor1";
            // 
            // EditFilters
            // 
            this.Controls.Add(this.filterEditor1);
            this.Name = "EditFilters";
            resources.ApplyResources(this, "$this");
            this.ResumeLayout(false);

        }

        #endregion

        private Duplicati.GUI.HelperControls.FilterEditor filterEditor1;


    }
}
