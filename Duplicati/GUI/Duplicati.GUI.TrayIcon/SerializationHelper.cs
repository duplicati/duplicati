using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.GUI.TrayIcon
{
    public enum RunnerState
    {
        Started,
        Suspended,
        Running,
        Stopped,
    }

    public enum LiveControlState
    {
        Running,
        Paused
    }
}
