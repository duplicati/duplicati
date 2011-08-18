using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Scheduler.Utility
{
    /// <summary>
    /// Puts up a generic calander and allows selection of multiple days and/or 'last day'
    /// </summary>
    public partial class DayPicker : UserControl
    {
        /// <summary>
        /// Fires when user clicks a day or last
        /// </summary>
        public event EventHandler CheckChanged;
        /// <summary>
        /// Is Last selected
        /// </summary>
        public bool Last { get { return this.LastCheckBox.Checked; } set { this.LastCheckBox.Checked = value; } }
        /// <summary>
        /// The days User selected
        /// </summary>
        public int[] DaysPicked 
        {
            get
            {
                return (from CheckBox qC in this.DayChecks where qC.Checked select int.Parse(qC.Text)).ToArray();
            }
            set
            {
                DayChecks.ForEach(qC => qC.Checked = value.Contains(int.Parse(qC.Text)));
            }
        }
        /// <summary>
        /// Days picked by user or 1 if none
        /// </summary>
        public int[] DaysPicked1IfEmpty
        {
            get
            {
                return (from CheckBox qC in this.DayChecks where qC.Checked select int.Parse(qC.Text)).DefaultIfEmpty(1).ToArray();
            }
        }
        /// <summary>
        /// Puts up a generic calander and allows selection of multiple days and/or 'last day'
        /// </summary>
        public DayPicker()
        {
            InitializeComponent();
            FillDates();
        }
        /// <summary>
        /// Checkboxes in handy list that does not include 'Last'
        /// </summary>
        private List<CheckBox> DayChecks = new List<CheckBox>();
        /// <summary>
        /// Make the control
        /// </summary>
        private void FillDates()
        {
            EventHandler Anon = (EventHandler)delegate(object sender, EventArgs e)
                    { if (CheckChanged != null) CheckChanged(this, e); };
            this.LastCheckBox.CheckedChanged += Anon;
            // 30 days hath...
            for (int d = 1; d < 32; d++)
            {
                CheckBox cb =
                    new CheckBox()
                    {
                        Appearance = System.Windows.Forms.Appearance.Button,
                        AutoSize = true,
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        Location = new System.Drawing.Point(4, 4),
                        Name = "checkBox"+d.ToString(),
                        Size = new System.Drawing.Size(20, 28),
                        TabIndex = 10+d,
                        Text = d.ToString(),
                        TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                        UseVisualStyleBackColor = true,
                        Checked = d==1,
                    };
                cb.CheckedChanged += Anon;
                DayChecks.Add(cb);
                this.tableLayoutPanel1.Controls.Add(cb);
            }
        }
        /// <summary>
        /// Pressed CLEAR, remove all checks
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ClearButton_Click(object sender, EventArgs e)
        {
            foreach (Control C in this.tableLayoutPanel1.Controls)
                if (C is CheckBox) ((CheckBox)C).Checked = false;
        }
    }
}
