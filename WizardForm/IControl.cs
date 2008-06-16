using System;
using System.Collections.Generic;
using System.Text;

namespace System.Windows.Forms.Wizard
{
    public interface IControl
    {
        void Displayed(IWizardForm owner);
    }
}
