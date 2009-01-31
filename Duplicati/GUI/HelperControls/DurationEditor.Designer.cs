namespace Duplicati.GUI.HelperControls
{
    partial class DurationEditor
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
            this.EasyDuration = new System.Windows.Forms.ComboBox();
            this.CustomDuration = new System.Windows.Forms.TextBox();
            this.errorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
            this.SuspendLayout();
            // 
            // EasyDuration
            // 
            this.EasyDuration.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.EasyDuration.FormattingEnabled = true;
            this.EasyDuration.Items.AddRange(new object[] {
            "Each day",
            "Each week",
            "Each second week",
            "Each month",
            "Custom ..."});
            this.EasyDuration.Location = new System.Drawing.Point(0, 0);
            this.EasyDuration.Name = "EasyDuration";
            this.EasyDuration.Size = new System.Drawing.Size(112, 21);
            this.EasyDuration.TabIndex = 0;
            this.EasyDuration.SelectedIndexChanged += new System.EventHandler(this.EasyDuration_SelectedIndexChanged);
            // 
            // CustomDuration
            // 
            this.errorProvider.SetError(this.CustomDuration, "s");
            this.CustomDuration.Location = new System.Drawing.Point(136, 0);
            this.CustomDuration.Name = "CustomDuration";
            this.CustomDuration.Size = new System.Drawing.Size(84, 20);
            this.CustomDuration.TabIndex = 1;
            this.CustomDuration.Visible = false;
            this.CustomDuration.TextChanged += new System.EventHandler(this.CustomDuration_TextChanged);
            // 
            // errorProvider
            // 
            this.errorProvider.ContainerControl = this;
            // 
            // DurationEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.CustomDuration);
            this.Controls.Add(this.EasyDuration);
            this.Name = "DurationEditor";
            this.Size = new System.Drawing.Size(221, 21);
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox EasyDuration;
        private System.Windows.Forms.TextBox CustomDuration;
        private System.Windows.Forms.ErrorProvider errorProvider;
    }
}
