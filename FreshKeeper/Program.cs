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

namespace FreshKeeper
{
    public static class Program
    {
        [STAThread()]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.DoEvents();

            if (Array.IndexOf(args, "ADMINISTRATION") == 0)
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.FileName = "updates.xml";
                dlg.Filter = "Update files (*.xml)|*.xml|All files (*.*)|*.*";
                dlg.CheckFileExists = false;
                dlg.CheckPathExists = true;
                if (dlg.ShowDialog() != DialogResult.OK)
                    return 0;

                UpdateAdministration dlg2 = new UpdateAdministration();
                dlg2.UpdateFile = dlg.FileName;
                Application.Run(dlg2);
                return 0;
            }

            FreshKeeper keeper = new FreshKeeper();
            keeper.Updateavailable += new FreshKeeper.UpdateavailableEvent(Updateavailable);
            keeper.UpdateError += new FreshKeeper.UpdateErrorEvent(ErrorOccured);
            keeper.CheckForUpdates(true);

            return 0;
        }

        private static void Updateavailable(FreshKeeper keeper, Update update)
        {
            ShowUpdateDetails frm = new ShowUpdateDetails();
            //frm.SetUpdate(update);
            Application.Run(frm);
        }

        private static void ErrorOccured(FreshKeeper keeper, Exception ex)
        {
            MessageBox.Show("Failed to check for updates: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
