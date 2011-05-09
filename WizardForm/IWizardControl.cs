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

namespace System.Windows.Forms.Wizard
{
    public interface IWizardControl
    {
        Control Control { get; }
        string Title { get; }
        string HelpText { get; }
        Image Image { get; }
        bool FullSize { get; }
        /// <summary>
        /// Called when the page is entering
        /// </summary>
        /// <param name="owner">The owner wizard form</param>
        /// <param name="args">State information</param>
        void Enter(IWizardForm owner, PageChangedArgs args);
        /// <summary>
        /// Called when the page has loaded and is being displayed
        /// </summary>
        /// <param name="owner">The owner wizard form</param>
        /// <param name="args">State information</param>
        void Display(IWizardForm owner, PageChangedArgs args);
        /// <summary>
        /// Called when the page is leaving
        /// </summary>
        /// <param name="owner">The owner wizard form</param>
        /// <param name="args">State information</param>
        void Leave(IWizardForm owner, PageChangedArgs args);
    }
}
