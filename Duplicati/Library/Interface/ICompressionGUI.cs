using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface used to provide a custom usercontrol for a compression module.
    /// This interface is not currently in use.
    /// </summary>
    public interface ICompressionGUI : ICompression, IGUIControl
    {
    }
}
