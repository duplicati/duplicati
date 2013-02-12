namespace Duplicati.GUI.Wizard_pages
{
    partial class GridContainer
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(GridContainer));
            this.OptionGrid = new Duplicati.GUI.HelperControls.CommandLineOptionGrid();
            this.SuspendLayout();
            // 
            // OptionGrid
            // 
            resources.ApplyResources(this.OptionGrid, "OptionGrid");
            this.OptionGrid.Name = "OptionGrid";
            // 
            // GridContainer
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.OptionGrid);
            this.Name = "GridContainer";
            this.ResumeLayout(false);

        }

        #endregion

        protected Duplicati.GUI.HelperControls.CommandLineOptionGrid OptionGrid;

    }
}
