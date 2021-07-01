//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using System.IO;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// This class encapsulates access to the backend and ensures at most one connection is active at a time
    /// </summary>
    internal class BackendHandler : SingleRunner
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BackendHandler>();

        public const string VOLUME_HASH = "SHA256";

        /// <summary>
        /// Data storage for an ongoing backend operation
        /// </summary>
        public class FileEntryItem
        {
            /// <summary>
            /// The current operation this entry represents
            /// </summary>
            public readonly BackendActionType Operation;
            /// <summary>
            /// The name of the remote file
            /// </summary>
            public string RemoteFilename;
            /// <summary>
            /// The name of the local file
            /// </summary>
            public string LocalFilename { get { return LocalTempfile; } }
            /// <summary>
            /// A reference to a temporary file that is disposed upon
            /// failure or completion of the item
            /// </summary>
            public TempFile LocalTempfile;
            /// <summary>
            /// True if the item has been encrypted
            /// </summary>
            public bool Encrypted;
            /// <summary>
            /// The expected hash value of the file
            /// </summary>
            public string Hash;
            /// <summary>
            /// The expected size of the file
            /// </summary>
            public long Size;
            /// <summary>
            /// A flag indicating if the file is a extra metadata file
            /// that has no entry in the database
            /// </summary>
            public bool TrackedInDb = true;
            /// <summary>
            /// A flag indicating if the operation is a retry run
            /// </summary>
            public bool IsRetry;

            public FileEntryItem(BackendActionType operation, string remotefilename)
            {
                Operation = operation;
                RemoteFilename = remotefilename;
                Size = -1;
            }

            public void SetLocalfilename(string name)
            {
                this.LocalTempfile = Library.Utility.TempFile.WrapExistingFile(name);
                this.LocalTempfile.Protected = true;
            }

            public void Encrypt(Options options)
            {
                if (!this.Encrypted && !options.NoEncryption)
                {
                    var tempfile = new Library.Utility.TempFile();
                    using (var enc = DynamicLoader.EncryptionLoader.GetModule(options.EncryptionModule, options.Passphrase, options.RawOptions))
                        enc.Encrypt(this.LocalFilename, tempfile);

                    this.DeleteLocalFile();

                    this.LocalTempfile = tempfile;
                    this.Hash = null;
                    this.Size = 0;
                    this.Encrypted = true;
                }
            }

            public TempFile CreateParity(Options options)
            {
                var tempfile = new Library.Utility.TempFile();
                using (var par = DynamicLoader.ParityLoader.GetModule(options.ParityModule, options.RawOptions))
                    par.Create(LocalFilename, tempfile.Name);

                return null;
            }

            public static string CalculateFileHash(string filename)
            {
                using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                using (var hasher = Duplicati.Library.Utility.HashAlgorithmHelper.Create(VOLUME_HASH))
                    return Convert.ToBase64String(hasher.ComputeHash(fs));
            }


            public bool UpdateHashAndSize(Options options)
            {
                if (Hash == null || Size < 0)
                {
                    Hash = CalculateFileHash(this.LocalFilename);
                    Size = new System.IO.FileInfo(this.LocalFilename).Length;
                    return true;
                }

                return false;
            }

            public void DeleteLocalFile()
            {
                if (this.LocalTempfile != null)
                {
                    try
                    {
                        this.LocalTempfile.Protected = false;
                        this.LocalTempfile.Dispose();
                    }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "DeleteTemporaryFileError", ex, "Failed to dispose temporary file: {0}", this.LocalTempfile); }
                    finally { this.LocalTempfile = null; }
                }
            }
        }
    }
}

