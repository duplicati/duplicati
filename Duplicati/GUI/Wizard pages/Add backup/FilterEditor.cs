using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class FilterEditor : System.Windows.Forms.Wizard.WizardControl
    {
        Task m_task;

        public FilterEditor()
            : base("Edit filters", "On this page you can modify filters that control what files are included in the backup.")
        {
            InitializeComponent();

            base.PageEnter += new System.Windows.Forms.Wizard.PageChangeHandler(FilterEditor_PageEnter);
            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(FilterEditor_PageLeave);
        }

        void FilterEditor_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (args.Direction == System.Windows.Forms.Wizard.PageChangedDirection.Back)
                return;

            List<TaskFilter> filters = new List<TaskFilter>();
            foreach (ListViewItem lvi in listView.Items)
            {
                if ((lvi.Tag as TaskFilter).DataParent == null)
                    m_task.DataParent.Add(lvi.Tag as TaskFilter);
                filters.Add(lvi.Tag as TaskFilter);
            }

            m_task.SortedFilters = filters.ToArray();

            args.NextPage = new Wizard_pages.Add_backup.FinishedAdd();
        }

        void FilterEditor_PageEnter(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            m_task = m_task = ((Schedule)m_settings["Schedule"]).Tasks[0];

            listView.Items.Clear();
            foreach (TaskFilter tf in m_task.SortedFilters)
            {
                ListViewItem lvi = new ListViewItem(tf.Filter, tf.Include ? 0 : 1);
                lvi.Tag = tf;
                listView.Items.Add(lvi);
            }

            if (listView.Items.Count > 0)
                listView.Items[0].Selected = true;
        }

        private void listView_SelectedIndexChanged(object sender, EventArgs e)
        {
            RemoveFilterButton.Enabled =
            EditFilterButton.Enabled = listView.SelectedItems.Count == 1;

            MoveFilterUpButton.Enabled =
            MoveFilterTopButton.Enabled = listView.SelectedItems.Count == 1 && listView.SelectedItems[0].Index != 0;

            MoveFilterDownButton.Enabled =
            MoveFilterBottomButton.Enabled = listView.SelectedItems.Count == 1 && listView.SelectedItems[0].Index != listView.Items.Count - 1;
        }

        private void RemoveFilterButton_Click(object sender, EventArgs e)
        {
            while(listView.SelectedItems.Count > 0)
                listView.Items.Remove(listView.SelectedItems[0]);
        }

        private void EditFilterButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1 && listView.SelectedItems[0].Tag is TaskFilter)
            {
                TaskFilter tf = listView.SelectedItems[0].Tag as TaskFilter;
                FilterDialog dlg = new FilterDialog(tf);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    listView.SelectedItems[0].Text = tf.Filter;
                    listView.SelectedItems[0].ImageIndex = tf.Include ? 0 : 1;
                }
            }
        }

        private void MoveFilterUpButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1)
            {
                ListViewItem lvi = listView.SelectedItems[0];
                int index = listView.SelectedItems[0].Index;
                listView.Items.RemoveAt(index);
                listView.Items.Insert(index - 1, lvi);
                lvi.Selected = true;
            }
        }

        private void MoveFilterDownButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1)
            {
                ListViewItem lvi = listView.SelectedItems[0];
                int index = listView.SelectedItems[0].Index;
                listView.Items.RemoveAt(index);
                listView.Items.Insert(index + 1, lvi);
                lvi.Selected = true;
            }
        }

        private void MoveFilterTopButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1)
            {
                ListViewItem lvi = listView.SelectedItems[0];
                int index = listView.SelectedItems[0].Index;
                listView.Items.RemoveAt(index);
                listView.Items.Insert(0, lvi);
                lvi.Selected = true;
            }
        }

        private void MoveFilterBottomButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1)
            {
                ListViewItem lvi = listView.SelectedItems[0];
                int index = listView.SelectedItems[0].Index;
                listView.Items.RemoveAt(index);
                listView.Items.Insert(listView.Items.Count, lvi);
                lvi.Selected = true;
            }
        }

        private void AddFilterButton_Click(object sender, EventArgs e)
        {
            TaskFilter tf = new TaskFilter();
            FilterDialog dlg = new FilterDialog(tf);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                ListViewItem lvi = new ListViewItem(tf.Filter, tf.Include ? 0 : 1);
                lvi.Tag = tf;
                listView.Items.Add(lvi);
            }

        }
    }
}

