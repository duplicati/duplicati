namespace Duplicati.GUI.HelperControls
{
    partial class SizeSelector
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SizeSelector));
            this.Suffix = new System.Windows.Forms.ComboBox();
            this.Number = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.Number)).BeginInit();
            this.SuspendLayout();
            // 
            // Suffix
            // 
            this.Suffix.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Suffix.FormattingEnabled = true;
            resources.ApplyResources(this.Suffix, "Suffix");
            this.Suffix.Name = "Suffix";
            this.Suffix.SelectedIndexChanged += new System.EventHandler(this.Suffix_SelectedIndexChanged);
            // 
            // Number
            // 
            resources.ApplyResources(this.Number, "Number");
            this.Number.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.Number.Name = "Number";
            this.Number.ValueChanged += new System.EventHandler(this.Number_ValueChanged);
            // 
            // SizeSelector
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Suffix);
            this.Controls.Add(this.Number);
            this.Name = "SizeSelector";
            ((System.ComponentModel.ISupportInitialize)(this.Number)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ComboBox Suffix;
        private System.Windows.Forms.NumericUpDown Number;
    }
}
