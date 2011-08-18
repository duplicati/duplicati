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
    public partial class MonthPicker : UserControl
    {
        public int[] MonthsPicked 
        {
            get
            {
                List<int> Picked = new List<int>();
                foreach(Control C in this.tableLayoutPanel1.Controls)
                    if(((CheckBox)C).Checked) Picked.Add(int.Parse(C.Text));
                return Picked.ToArray();
            }
            set
            {
                foreach (Control C in this.tableLayoutPanel1.Controls)
                    ((CheckBox)C).Checked = value.Contains(int.Parse(C.Text));
            }
        }
        public void ClearAll()
        {
            foreach (Control C in this.tableLayoutPanel1.Controls)
                ((CheckBox)C).Checked = false;
        }
        public void SelectAll()
        {
            foreach (Control C in this.tableLayoutPanel1.Controls)
                ((CheckBox)C).Checked = true;
        }
        public MonthPicker()
        {
            InitializeComponent();
            FillDates();
        }
        private void FillDates()
        {
            for (int d = 0; d < 13; d++)
                this.tableLayoutPanel1.Controls.Add(
                    new CheckBox()
                    {
                        Appearance = System.Windows.Forms.Appearance.Button,
                        AutoSize = true,
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        Location = new System.Drawing.Point(4, 4),
                        Name = "checkBox"+d.ToString(),
                        Size = new System.Drawing.Size(20, 28),
                        TabIndex = 10+d,
                        Text = System.Globalization.DateTimeFormatInfo.CurrentInfo.MonthNames[d],
                        TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                        UseVisualStyleBackColor = true,
                        Checked = false,
                    });
        }
    }
}
