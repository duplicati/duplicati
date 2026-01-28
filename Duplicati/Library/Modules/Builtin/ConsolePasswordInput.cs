// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;

namespace Duplicati.Library.Modules.Builtin
{
    /// <summary>
    /// Module for handling console-based password input.
    /// </summary>
    public class ConsolePasswordInput : Duplicati.Library.Interface.IGenericModule
    {
        /// <summary>
        /// These actions only use the local database and do not require access to the data inside the files.
        /// For List and ListChanges this may not be true if there is no local database
        /// </summary>
        private readonly static string[] PASSPHRASELESS_ACTIONS = { "CreateLogDb", "TestFilters", "ListAffected", "SystemInfo", "SendMail" };

        /// <summary>
        /// The option used to force stdin reading
        /// </summary>
        private const string FORCE_PASSPHRASE_FROM_STDIN_OPTION = "force-passphrase-from-stdin";

        #region IGenericModule Members

        /// <summary>
        /// Gets the key identifier for this module.
        /// </summary>
        public string Key { get { return "console-password-input"; } }
        /// <summary>
        /// Gets the display name for this module.
        /// </summary>
        public string DisplayName { get { return Strings.ConsolePasswordInput.Displayname; } }
        /// <summary>
        /// Gets the description of this module.
        /// </summary>
        public string Description { get { return Strings.ConsolePasswordInput.Description; } }
        /// <summary>
        /// Gets whether this module should be loaded by default.
        /// </summary>
        public bool LoadAsDefault { get { return true; } }
        /// <summary>
        /// Gets the list of supported command line arguments.
        /// </summary>
        public IList<Interface.ICommandLineArgument> SupportedCommands
            => [
                new Interface.CommandLineArgument(FORCE_PASSPHRASE_FROM_STDIN_OPTION, Interface.CommandLineArgument.ArgumentType.Boolean, Strings.ConsolePasswordInput.ForcepassphrasefromstdinShort, Strings.ConsolePasswordInput.ForcepassphrasefromstdinLong)
            ];

        /// <summary>
        /// Configures the module with the provided command line options.
        /// </summary>
        /// <param name="commandlineOptions">The command line options dictionary.</param>
        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            //Ensure the setup is valid, could throw an exception
            if (!commandlineOptions.ContainsKey("main-action"))
                return;

            //First see if a password is actually required for the action
            foreach (string s in PASSPHRASELESS_ACTIONS)
                if (string.Equals(s, commandlineOptions["main-action"], StringComparison.OrdinalIgnoreCase))
                    return;

            //See if a password is already present or encryption is disabled
            if (!commandlineOptions.ContainsKey("passphrase") && !Duplicati.Library.Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), "no-encryption"))
            {
                // Check if we need confirmation
                var confirm = string.Equals(commandlineOptions["main-action"], "backup", StringComparison.OrdinalIgnoreCase);

                // Bypass the TTY input if requested
                if (Library.Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), FORCE_PASSPHRASE_FROM_STDIN_OPTION))
                {
                    commandlineOptions["passphrase"] = ReadPassphraseFromStdin(confirm);
                }
                else
                {
                    //Get the passphrase, try with TTY first
                    try
                    {
                        commandlineOptions["passphrase"] = ReadPassphraseFromConsole(confirm);
                    }
                    catch (InvalidOperationException)
                    {
                        // Handle redirect issues on Windows only
                        if (!OperatingSystem.IsWindows())
                            throw;

                        commandlineOptions["passphrase"] = ReadPassphraseFromStdin(confirm);
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// Reads the passphrase from standard input.
        /// </summary>
        /// <param name="confirm">Whether to confirm the passphrase.</param>
        /// <returns>The passphrase.</returns>
        private static string ReadPassphraseFromStdin(bool confirm)
        {
            var passphrase = Console.ReadLine();
            if (confirm)
            {
                Console.Write("\n" + Strings.ConsolePasswordInput.ConfirmPassphrasePrompt + ": ");
                var password2 = Console.ReadLine();

                if (passphrase != password2)
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.ConsolePasswordInput.PassphraseMismatchError, "PassphraseMismatch");
            }

            if (string.IsNullOrWhiteSpace(passphrase))
                throw new Duplicati.Library.Interface.UserInformationException(Strings.ConsolePasswordInput.EmptyPassphraseError, "EmptyPassphrase");

            return passphrase;
        }

        /// <summary>
        /// Reads the passphrase from the console.
        /// </summary>
        /// <param name="confirm">Whether to confirm the passphrase.</param>
        /// <returns>The passphrase.</returns>
        private static string ReadPassphraseFromConsole(bool confirm)
        {
            // First entry (includes prompt and masking)
            var passphrase = Utility.Utility.ReadSecretFromConsole("\n" + Strings.ConsolePasswordInput.EnterPassphrasePrompt + ": ");

            if (confirm)
            {
                // Confirmation entry
                var password2 = Utility.Utility.ReadSecretFromConsole(Strings.ConsolePasswordInput.ConfirmPassphrasePrompt + ": ");

                if (!string.Equals(passphrase, password2, StringComparison.Ordinal))
                    throw new Interface.UserInformationException(Strings.ConsolePasswordInput.PassphraseMismatchError, "PassphraseMismatch");
            }

            if (string.IsNullOrWhiteSpace(passphrase))
                throw new Interface.UserInformationException(Strings.ConsolePasswordInput.EmptyPassphraseError, "EmptyPassphrase");

            return passphrase;
        }
    }
}
