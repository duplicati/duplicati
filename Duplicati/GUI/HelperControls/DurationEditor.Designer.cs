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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DurationEditor));
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
            resources.GetString("EasyDuration.Items"),
            resources.GetString("EasyDuration.Items1"),
            resources.GetString("EasyDuration.Items2"),
            resources.GetString("EasyDuration.Items3"),
            resources.GetString("EasyDuration.Items4")});
            resources.ApplyResources(this.EasyDuration, "EasyDuration");
            this.EasyDuration.Name = "EasyDuration";
            this.EasyDuration.SelectedIndexChanged += new System.EventHandler(this.EasyDuration_SelectedIndexChanged);
            // 
            // CustomDuration
            // 
            this.errorProvider.SetError(this.CustomDuration, resources.GetString("CustomDuration.Error"));
            this.errorProvider.SetIconPadding(this.CustomDuration, ((int)(resources.GetObject("CustomDuration.IconPadding"))));
            resources.ApplyResources(this.CustomDuration, "CustomDuration");
            this.CustomDuration.Name = "CustomDuration";
            this.CustomDuration.TextChanged += new System.EventHandler(this.CustomDuration_TextChanged);
            // 
            // errorProvider
            // 
            this.errorProvider.ContainerControl = this;
            // 
            // DurationEditor
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.CustomDuration);
            this.Controls.Add(this.EasyDuration);
            this.Name = "DurationEditor";
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
