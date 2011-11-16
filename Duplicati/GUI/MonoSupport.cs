#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
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
		
		/// <summary>
		/// Custom implementation of the BeginInvoke method, as Mono does the same for Invoke and BeginInvoke
		/// </summary>
		/// <param name='method'>The method to invoke</param>
		/// <param name='args'>The parameters to call the method with</param>
		public static void BeginInvoke(Control owner, Delegate method, params object[] args)
		{
			new System.Threading.Thread(BeginInvokeHelper).Start (new object[] { owner, method, args });
		}
		
		/// <summary>
		/// Helper method to do invocations from a thread
		/// </summary>
		/// <param name='arg'>
		/// An object array where the first entry is the control owner, 
		/// the second is the delegate and the third is the arguments
		/// </param>
		private static void BeginInvokeHelper(object arg)
		{
			try 
			{
				System.Threading.Thread.Sleep(500);
				object[] args = (object[])arg;
				Control owner = (Control)args[0];
				Delegate delg = (Delegate)args[1];
				object[] input = (object[])args[2];
				
				owner.Invoke(delg, input);
			} 
			catch 
			{
				
			}
		}
    }
}
