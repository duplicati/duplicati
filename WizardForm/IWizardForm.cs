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
using System.Drawing;
using System.ComponentModel;

namespace System.Windows.Forms.Wizard
{
    public enum PageChangedDirection
    {
        Next,
        Back
    }

    public class PageChangedArgs : CancelEventArgs
    {
        public PageChangedArgs(IWizardForm owner, bool treatAsLast, PageChangedDirection direction)
            : base()
        {
            this.Owner = owner;
            this.Settings = owner.Settings;
            this.Direction = direction;
            this.TreatAsLast = treatAsLast;
        }

        public IWizardControl NextPage;
        public bool TreatAsLast;
        public PageChangedDirection Direction;
        public Dictionary<string, object> Settings;
        public IWizardForm Owner;

    }

    public delegate void PageChangeHandler(object sender, PageChangedArgs args);

    public interface IWizardForm
    {
        Button NextButton { get; }
        Button BackButton { get; }
        Button CancelButton { get; }
        Panel ButtonPanel { get; }
        GroupBox ContentPanel { get; }
        Panel InfoPanel { get; }
        PictureBox PageIcon { get; }
        Label InfoLabel { get; }
        Label TitleLabel { get; }
        Dialog Dialog { get; }
        List<IWizardControl> Pages { get; }
        IWizardControl CurrentPage { get;}
        string Title { get; set; }
        Image DefaultImage { get; set; }
        void UpdateButtons();
        void UpdateDisplay();
        Dictionary<string, object> Settings { get; }

        event PageChangeHandler NextPressed;
        event PageChangeHandler BackPressed;
        event CancelEventHandler Finished;
        event CancelEventHandler Cancelled;
    }
}
