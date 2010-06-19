using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface for modules that register a control in the settings menu.
    /// This control will be placed in a tab page.
    /// The <see cref="GetConfiguration"/> method is invoked for each custom control module when generating a backup.
    /// When editing/creating a backup job, the <see cref="BeginEdit"/> and <see cref="EndEdit"/> functions are invoked.
    /// </summary>
    public interface ISettingsControl : IGUIControl
    {
        /// <summary>
        /// Gets a key that uniquely identifies the control
        /// </summary>
        string Key { get; }

        /// <summary>
        /// Called when the user is editing or creating a backup.
        /// This method can alter the applicationSettings environment and thus communicate with another UI plugin,
        /// such as an <see cref="IBackendGUI"/> or <see cref="IEncryptionGUI"/>.
        /// </summary>
        /// <param name="applicationSettings">The application settings</param>
        /// <param name="guiOptions">The options saved earlier when configuring the control</param>
        void BeginEdit(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions);

        /// <summary>
        /// Called when the user has finished editing or creating a backup.
        /// This method can be used to alter the <see cref="guiOptions"/> collection,
        /// and thus persist a change.
        /// As the other elements can also modify the <see cref="applicationSettings"/> collection,
        /// this can be used to communicate changes from another component.
        /// </summary>
        /// <param name="applicationSettings">The application settings</param>
        /// <param name="guiOptions">The options saved earlier when configuring the control</param>
        void EndEdit(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions);
    }
}
