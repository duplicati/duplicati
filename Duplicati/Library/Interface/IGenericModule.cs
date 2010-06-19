using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface for a plugable generic module.
    /// An instance of a module is loaded prior to a backup or restore operation,
    /// and can perform tasks relating to the general execution environment, as
    /// well as modify the options used in Duplicati.
    /// </summary>
    public interface IGenericModule
    {
        /// <summary>
        /// The module key, used to activate or deactivate the module on the commandline
        /// </summary>
        string Key { get; }

        /// <summary>
        /// A localized string describing the module with a friendly name
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// A localized description of the module
        /// </summary>
        string Description { get; }

        /// <summary>
        /// A boolean value that indicates if the module should always be loaded.
        /// If true, the  user can choose to not load the module by entering the appropriate commandline option.
        /// If false, the user can choose to load the module by entering the appropriate commandline option.
        /// </summary>
        bool LoadAsDefault { get; }

        /// <summary>
        /// Gets a list of supported commandline arguments
        /// </summary>
        IList<ICommandLineArgument> SupportedCommands { get; }

        /// <summary>
        /// This method is the interception where the module can interact with the execution environment and modify the settings.
        /// </summary>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        void Configure(IDictionary<string, string> commandlineOptions);
    }
}
