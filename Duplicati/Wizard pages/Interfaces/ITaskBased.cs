using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Datamodel;

namespace Duplicati.Wizard_pages.Interfaces
{
    interface ITaskBased
    {
        void Setup(Task task);
    }
}
