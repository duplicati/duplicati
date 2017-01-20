#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Modules.Builtin
{
    public class ConsolePasswordInput : Duplicati.Library.Interface.IGenericModule
    {
        /// <summary>
        /// These actions only use the local database and do not require access to the data inside the files.
        /// For List and ListChanges this may not be true if there is no local database
        /// </summary>
        private readonly static string[] PASSPHRASELESS_ACTIONS = { "CreateLogDb", "TestFilters", "ListAffected", "SystemInfo", "SendMail" };

        #region IGenericModule Members

        public string Key { get { return "console-password-input"; } }
        public string DisplayName { get { return Strings.ConsolePasswordInput.Displayname; } }
        public string Description { get { return Strings.ConsolePasswordInput.Description; } }
        public bool LoadAsDefault { get { return true; } }
        public IList<Duplicati.Library.Interface.ICommandLineArgument> SupportedCommands { get { return null; } }

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            //Ensure the setup is valid, could throw an exception
            if (!commandlineOptions.ContainsKey("main-action"))
                return;

            //First see if a password is actually required for the action
            foreach (string s in PASSPHRASELESS_ACTIONS)
                if (string.Equals(s, commandlineOptions["main-action"], StringComparison.InvariantCultureIgnoreCase))
                    return;

            //See if a password is already present or encryption is disabled
            if (!commandlineOptions.ContainsKey("passphrase") && !Duplicati.Library.Utility.Utility.ParseBoolOption(commandlineOptions, "no-encryption"))
            {
                //Get the passphrase
                bool confirm = string.Equals(commandlineOptions["main-action"], "backup", StringComparison.InvariantCultureIgnoreCase);
                commandlineOptions["passphrase"] = ReadPassphraseFromConsole(confirm);
            }
        }

        #endregion

        private static string ReadPassphraseFromConsole(bool confirm)
        {
            Console.Write("\n" + Strings.ConsolePasswordInput.EnterPassphrasePrompt + ": ");
            StringBuilder passphrase = new StringBuilder();

            while (true)
            {
                ConsoleKeyInfo k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter)
                    break;

                if (k.Key == ConsoleKey.Escape)
                    throw new Library.Interface.CancelException("");

                if (k.KeyChar != '\0') passphrase.Append(k.KeyChar);

                //Unix/Linux user know that there is no feedback, Win user gets scared :)
                if (System.Environment.OSVersion.Platform != PlatformID.Unix)
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

                    //Unix/Linux user know that there is no feedback, Win user gets scared :)
                    if (System.Environment.OSVersion.Platform != PlatformID.Unix)
                        Console.Write("*");
                }
                Console.WriteLine();

                if (passphrase.ToString() != password2.ToString())
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.ConsolePasswordInput.PassphraseMismatchError);
            }

            if (passphrase.ToString().Length == 0)
                throw new Duplicati.Library.Interface.UserInformationException(Strings.ConsolePasswordInput.EmptyPassphraseError);

            return passphrase.ToString();
        }
    }
}
