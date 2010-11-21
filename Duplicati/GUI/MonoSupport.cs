using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI
{
    public static class MonoSupport
    {
        /// <summary>
        /// Fixes a bug in Mono MWF support for localized forms and multiline textboxes:
        /// https://bugzilla.novell.com/show_bug.cgi?id=440191
        /// </summary>
        /// <param name="owner">The toplevel control to run the procedure on</param>
        public static void FixTextBoxes(Control owner)
        {
            FixTextBoxes(owner, new System.ComponentModel.ComponentResourceManager(owner.GetType()));
        }

        /// <summary>
        /// Fixes a bug in Mono MWF support for localized forms and multiline textboxes:
        /// https://bugzilla.novell.com/show_bug.cgi?id=440191
        /// </summary>
        /// <param name="owner">The toplevel control to run the procedure on</param>
        public static void FixTextBoxes(Control owner, System.ComponentModel.ComponentResourceManager resources)
        {
            try
            {
                if (!Duplicati.Library.Utility.Utility.IsClientLinux)
                    return;

                Dictionary<Control, Control> visited = new Dictionary<Control, Control>();
                Queue<Control> work = new Queue<Control>();
                work.Enqueue(owner);

                while (work.Count > 0)
                {
                    Control c = work.Dequeue();
                    visited[c] = null;

                    if (c.HasChildren)
                        foreach (Control cc in c.Controls)
                            if (!visited.ContainsKey(cc))
                            {
                                work.Enqueue(cc);
                                visited[cc] = null;
                            }

                    if (c is TextBox && ((TextBox)c).Multiline)
                        resources.ApplyResources(c, c.Name);
                }
            }
            finally
            {
                if (resources != null)
                    resources.ReleaseAllResources();
            }
        }

    }
}
