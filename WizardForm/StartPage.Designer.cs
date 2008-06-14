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
            this.panel1 = new System.Windows.Forms.Panel();
            this.panel2 = new System.Windows.Forms.Panel();
            this.WizardHelp = new System.Windows.Forms.Label();
            this.WizardTitle = new System.Windows.Forms.Label();
            this.WizardIntroduction = new System.Windows.Forms.Label();
            this.panel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.Navy;
            this.panel1.Dock = System.Windows.Forms.DockStyle.Left;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(152, 312);
            this.panel1.TabIndex = 0;
            // 
            // panel2
            // 
            this.panel2.BackColor = System.Drawing.Color.White;
            this.panel2.Controls.Add(this.WizardIntroduction);
            this.panel2.Controls.Add(this.WizardTitle);
            this.panel2.Controls.Add(this.WizardHelp);
            this.panel2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel2.Location = new System.Drawing.Point(152, 0);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(354, 312);
            this.panel2.TabIndex = 1;
            // 
            // WizardHelp
            // 
            this.WizardHelp.AutoSize = true;
            this.WizardHelp.Location = new System.Drawing.Point(8, 280);
            this.WizardHelp.Name = "WizardHelp";
            this.WizardHelp.Size = new System.Drawing.Size(109, 13);
            this.WizardHelp.TabIndex = 0;
            this.WizardHelp.Text = "Click next to continue";
            // 
            // WizardTitle
            // 
            this.WizardTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.WizardTitle.Location = new System.Drawing.Point(16, 16);
            this.WizardTitle.Name = "WizardTitle";
            this.WizardTitle.Size = new System.Drawing.Size(320, 48);
            this.WizardTitle.TabIndex = 1;
            this.WizardTitle.Text = "Wizard title";
            // 
            // WizardIntroduction
            // 
            this.WizardIntroduction.Location = new System.Drawing.Point(16, 64);
            this.WizardIntroduction.Name = "WizardIntroduction";
            this.WizardIntroduction.Size = new System.Drawing.Size(320, 208);
            this.WizardIntroduction.TabIndex = 2;
            this.WizardIntroduction.Text = "Wizard introduction";
            // 
            // StartPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.panel1);
            this.Name = "StartPage";
            this.Size = new System.Drawing.Size(506, 312);
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
