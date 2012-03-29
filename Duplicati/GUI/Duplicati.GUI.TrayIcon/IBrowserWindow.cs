using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.GUI.TrayIcon
{
    public interface IBrowserWindow
    {
        string Title { set; }
        WindowIcons Icon { set; }
    }
}
