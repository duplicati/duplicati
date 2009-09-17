using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Gives access to localized versions of common strings.
    /// </summary>
    public class CommonStrings
    {
        //This class exists because the ResXGenTool makes all members "internal"

        /// <summary>
        ///   Looks up a localized string similar to Do you want to test the connection?.
        /// </summary>
        public static string ConfirmTestConnectionQuestion
        {
            get
            {
                return Strings.Common.ConfirmTestConnectionQuestion;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to Connection Failed: {0}.
        /// </summary>
        public static string ConnectionFailure
        {
            get
            {
                return Strings.Common.ConnectionFailure;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to Connection succeeded!.
        /// </summary>
        public static string ConnectionSuccess
        {
            get
            {
                return Strings.Common.ConnectionSuccess;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to You have not entered a path. This will store all backups in the default directory. Is this what you want?.
        /// </summary>
        public static string DefaultDirectoryWarning
        {
            get
            {
                return Strings.Common.DefaultDirectoryWarning;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to You must enter a password.
        /// </summary>
        public static string EmptyPasswordError
        {
            get
            {
                return Strings.Common.EmptyPasswordError;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to You have not entered a password.\nProceed without a password?.
        /// </summary>
        public static string EmptyPasswordWarning
        {
            get
            {
                return Strings.Common.EmptyPasswordWarning;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to You must enter the name of the server.
        /// </summary>
        public static string EmptyServernameError
        {
            get
            {
                return Strings.Common.EmptyServernameError;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to You must enter a username.
        /// </summary>
        public static string EmptyUsernameError
        {
            get
            {
                return Strings.Common.EmptyUsernameError;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to You have not entered a username.\nThis is fine if the server allows anonymous uploads, but likely a username is required\nProceed without a username?.
        /// </summary>
        public static string EmptyUsernameWarning
        {
            get
            {
                return Strings.Common.EmptyUsernameWarning;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to Folder created!.
        /// </summary>
        public static string FolderCreated
        {
            get
            {
                return Strings.Common.FolderCreated;
            }
        }

    }
}
