using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface used to provide a custom usercontrol for an encryption module.
    /// The control is displayed on the passphrase selection page, and thus has limited space avalible.
    /// If this interface is not implemented, a default options grid will be displayed.
    /// To prevent options from being displayed, implement this interface, and return an empty control in the <see cref="GetControl"/> method.
    /// </summary>
    public interface IEncryptionGUI : IEncryption, IGUIControl
    {
    }
}
