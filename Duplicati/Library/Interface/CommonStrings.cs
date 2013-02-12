#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.Library.Interface
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

        /// <summary>
        ///   Looks up a localized string similar to The configuration for the backend is not valid, it is missing the {0} field.
        /// </summary>
        public static string ConfigurationIsMissingItemError
        {
            get
            {
                return Strings.Common.ConfigurationIsMissingItemError;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to The requested folder does not exist.
        /// </summary>
        public static string FolderMissingError
        {
            get
            {
                return Strings.Common.FolderMissingError;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to The folder cannot be created because it already exists.
        /// </summary>
        public static string FolderAlreadyExistsError
        {
            get
            {
                return Strings.Common.FolderAlreadyExistsError;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to The connection succeeded but another backup was found in the destination folder. It is possible to configure Duplicati to store multiple backups in the same folder, but it is not recommended.
        ///
        ///Do you want to use the selected folder?.
        /// </summary>
        public static string ExistingBackupDetectedQuestion
        {
            get
            {
                return Strings.Common.ExistingBackupDetectedQuestion;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to The server name &quot;{0}&quot; is not valid.
        /// </summary>
        public static string InvalidServernameError
        {
            get
            {
                return Strings.Common.InvalidServernameError;
            }
        }
    }
}
