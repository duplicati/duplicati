namespace Duplicati.GUI.Wizard_pages
{
    partial class SelectBackend
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SelectBackend));
            this.Question = new System.Windows.Forms.Label();
            this.BackendList = new System.Windows.Forms.Panel();
            this.toolTips = new System.Windows.Forms.ToolTip(this.components);
            this.SuspendLayout();
            // 
            // Question
            // 
            resources.ApplyResources(this.Question, "Question");
            this.Question.Name = "Question";
            // 
            // BackendList
            // 
            resources.ApplyResources(this.BackendList, "BackendList");
            this.BackendList.Name = "BackendList";
            // 
            // SelectBackend
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.BackendList);
            this.Controls.Add(this.Question);
            this.Name = "SelectBackend";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label Question;
        private System.Windows.Forms.Panel BackendList;
        private System.Windows.Forms.ToolTip toolTips;
    }
}
