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
using System.Text;
using Duplicati.Library.Common;

namespace Duplicati.Library.Modules.Builtin
{
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

        public string Key { get { return "console-password-input"; } }
        public string DisplayName { get { return Strings.ConsolePasswordInput.Displayname; } }
        public string Description { get { return Strings.ConsolePasswordInput.Description; } }
        public bool LoadAsDefault { get { return true; } }
        public IList<Duplicati.Library.Interface.ICommandLineArgument> SupportedCommands
            => new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument(FORCE_PASSPHRASE_FROM_STDIN_OPTION, Interface.CommandLineArgument.ArgumentType.Boolean, Strings.ConsolePasswordInput.ForcepassphrasefromstdinShort, Strings.ConsolePasswordInput.ForcepassphrasefromstdinLong)
                };

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
                // Print a banner
                Console.Write("\n" + Strings.ConsolePasswordInput.EnterPassphrasePrompt + ": ");

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

        private static string ReadPassphraseFromConsole(bool confirm)
        {
            StringBuilder passphrase = new StringBuilder();

            while (true)
            {
                ConsoleKeyInfo k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter)
                    break;

                if (k.Key == ConsoleKey.Escape)
                    throw new Interface.CancelException("");

                if (k.KeyChar != '\0')
                    passphrase.Append(k.KeyChar);

                // Provide feedback to the user
                Console.Write("*");
            }

            Console.WriteLine();

            if (confirm)
            {
                Console.Write("\n" + Strings.ConsolePasswordInput.ConfirmPassphrasePrompt + ": ");
                StringBuilder password2 = new StringBuilder();

                while (true)
                {
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Enter)
                        break;

                    if (k.Key == ConsoleKey.Escape)
                        return null;

                    password2.Append(k.KeyChar);

                    // Provide feedback to the user
                    Console.Write("*");
                }
                Console.WriteLine();

                if (passphrase.ToString() != password2.ToString())
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.ConsolePasswordInput.PassphraseMismatchError, "PassphraseMismatch");
            }

            if (string.IsNullOrWhiteSpace(passphrase.ToString()))
                throw new Duplicati.Library.Interface.UserInformationException(Strings.ConsolePasswordInput.EmptyPassphraseError, "EmptyPassphrase");

            return passphrase.ToString();
        }
    }
}
