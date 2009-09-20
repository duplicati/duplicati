using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// The interface used to display a GUI wizard page for the user.
    /// If the backed should be avalible from the GUI, this interface must be implemented
    /// </summary>
    public interface IBackendGUI : IBackend
    {
        /// <summary>
        /// The title of the page in the wizard
        /// </summary>
        string PageTitle { get; }

        /// <summary>
        /// The page description in the wizard
        /// </summary>
        string PageDescription { get; }

        /// <summary>
        /// A method to retrieve a user interface control.
        /// The method should keep a reference to the dictionary.
        /// The control will be resized to fit in the wizard.
        /// </summary>
        /// <param name="applicationSettings">The set of settings defined by the calling application</param>
        /// <param name="options">A set of options, either empty or previously stored by the backend</param>
        /// <returns>The control that represents the user interface</returns>
        Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options);

        /// <summary>
        /// Method that is called when the user leaves the form.
        /// This method can be used to store data from the interface in the dictionary,
        /// so the user interface can be reconstructed later.
        /// </summary>
        /// <param name="control">The UI object created by the GetControl call</param>
        void Leave(Control control);

        /// <summary>
        /// A method that is called when the form should be validated.
        /// The method should inform the user if the validation fails.
        /// </summary>
        /// <param name="control">The UI object created by the GetControl call</param>
        /// <returns>True if the data is valid, false otherwise</returns>
        bool Validate(Control control);

        /// <summary>
        /// Returns the target path for activating Duplicati, and sets any options required.
        /// </summary>
        /// <param name="applicationSettings">The set of settings defined by the calling application</param>
        /// <param name="guiOptions">A set of previously saved options for the backend</param>
        /// <param name="commandlineOptions">A set of commandline options for the backend</param>
        /// <returns>The destination path</returns>
        string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions);
    }
}
