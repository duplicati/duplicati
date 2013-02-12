namespace Duplicati.GUI.Wizard_pages.Backends
{
    partial class RawContainer
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RawContainer));
            this.label1 = new System.Windows.Forms.Label();
            this.Destination = new System.Windows.Forms.TextBox();
            this.ProtocolKey = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // OptionGrid
            // 
            resources.ApplyResources(this.OptionGrid, "OptionGrid");
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // Destination
            // 
            resources.ApplyResources(this.Destination, "Destination");
            this.Destination.Name = "Destination";
            // 
            // ProtocolKey
            // 
            resources.ApplyResources(this.ProtocolKey, "ProtocolKey");
            this.ProtocolKey.Name = "ProtocolKey";
            this.ProtocolKey.ReadOnly = true;
            // 
            // RawContainer
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Destination);
            this.Controls.Add(this.ProtocolKey);
            this.Controls.Add(this.label1);
            this.Name = "RawContainer";
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.ProtocolKey, 0);
            this.Controls.SetChildIndex(this.Destination, 0);
            this.Controls.SetChildIndex(this.OptionGrid, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Destination;
        private System.Windows.Forms.TextBox ProtocolKey;
    }
}
