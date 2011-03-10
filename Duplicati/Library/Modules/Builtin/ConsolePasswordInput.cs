using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Modules.Builtin
{
    public class ConsolePasswordInput : Duplicati.Library.Interface.IGenericModule
    {
        /// <summary>
        /// These actions only use the file list and do not require access to the data inside the files,
        /// and thus require no encryption setup
        /// </summary>
        private readonly static string[] PASSPHRASELESS_ACTIONS = { "List", "DeleteAllButNFull", "DeleteOlderThan", "Cleanup", "GetBackupSets", "CreateFolder" };

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
            if (!commandlineOptions.ContainsKey("passphrase") && !commandlineOptions.ContainsKey("no-encryption"))
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

                passphrase.Append(k.KeyChar);

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
                    throw new Exception(Strings.ConsolePasswordInput.PassphraseMismatchError);
            }

            if (passphrase.ToString().Length == 0)
                throw new Exception(Strings.ConsolePasswordInput.EmptyPassphraseError);

            return passphrase.ToString();
        }
    }
}
