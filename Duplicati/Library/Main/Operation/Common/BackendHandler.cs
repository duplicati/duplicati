// Copyright (C) 2024, The Duplicati Team
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
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using System.IO;
using System.Security.Cryptography;

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

            public static string CalculateFileHash(string filename)
            {
                using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                using (var hasher = VolumeHashFactory.CreateHasher())
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

