using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface that indicates that the control does not require a full page.
    /// If the host application chooses to do so, it may display a collection of
    /// mini controls on a single page, rather than display them on a page each.
    /// This makes the UI less cluttered if there are many small custom controls.
    /// A control should implement this interface if it requires only one or two
    /// lines, eg. less than 40 pixels high.
    /// </summary>
    public interface IGUIMiniControl : IGUIControl
    {
    }
}
