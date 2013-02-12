namespace System.Windows.Forms.Wizard
{
    partial class StartPage
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(StartPage));
            this.panel1 = new System.Windows.Forms.Panel();
            this.panel2 = new System.Windows.Forms.Panel();
            this.WizardIntroduction = new System.Windows.Forms.Label();
            this.WizardTitle = new System.Windows.Forms.Label();
            this.WizardHelp = new System.Windows.Forms.Label();
            this.panel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.Navy;
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.Name = "panel1";
            // 
            // panel2
            // 
            this.panel2.BackColor = System.Drawing.Color.White;
            this.panel2.Controls.Add(this.WizardIntroduction);
            this.panel2.Controls.Add(this.WizardTitle);
            this.panel2.Controls.Add(this.WizardHelp);
            resources.ApplyResources(this.panel2, "panel2");
            this.panel2.Name = "panel2";
            // 
            // WizardIntroduction
            // 
            resources.ApplyResources(this.WizardIntroduction, "WizardIntroduction");
            this.WizardIntroduction.Name = "WizardIntroduction";
            // 
            // WizardTitle
            // 
            resources.ApplyResources(this.WizardTitle, "WizardTitle");
            this.WizardTitle.Name = "WizardTitle";
            // 
            // WizardHelp
            // 
            resources.ApplyResources(this.WizardHelp, "WizardHelp");
            this.WizardHelp.Name = "WizardHelp";
            // 
            // StartPage
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.panel1);
            this.Name = "StartPage";
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private Panel panel1;
        private Panel panel2;
        private Label WizardIntroduction;
        private Label WizardTitle;
        private Label WizardHelp;
    }
}
