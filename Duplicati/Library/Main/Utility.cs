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
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Text;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;
using System.Linq;

namespace Duplicati.Library.Main
{
    public class Utility
    {
        /// <summary>
        /// Implementation of the IMetahash interface
        /// </summary>
        private class Metahash : IMetahash
        {
            /// <summary>
            /// The base64 encoded hash
            /// </summary>
            private readonly string m_filehash;
            /// <summary>
            /// The UTF-8 encoded json element with the metadata
            /// </summary>
            private readonly byte[] m_blob;
            /// <summary>
            /// The lookup table with elements
            /// </summary>
            private readonly Dictionary<string, string> m_values;

            public Metahash(Dictionary<string, string> values, Options options)
            {
                m_values = values;

                using (var ms = new System.IO.MemoryStream())
                using (var w = new StreamWriter(ms, Encoding.UTF8))
                using (var filehasher = HashFactory.CreateHasher(options.FileHashAlgorithm))
                {
                    if (filehasher == null)
                        throw new Duplicati.Library.Interface.UserInformationException(Strings.Common.InvalidHashAlgorithm(options.FileHashAlgorithm), "FileHashAlgorithmNotSupported");

                    w.Write(JsonConvert.SerializeObject(values));
                    w.Flush();

                    m_blob = ms.ToArray();

                    ms.Position = 0;
                    m_filehash = Convert.ToBase64String(filehasher.ComputeHash(ms));
                }
            }

            public string FileHash
            {
                get { return m_filehash; }
            }

            public byte[] Blob
            {
                get { return m_blob; }
            }

            public Dictionary<string, string> Values
            {
                get { return m_values; }
            }
        }

        /// <summary>
        /// Constructs a container for a given metadata dictionary
        /// </summary>
        /// <param name="values">The metadata values to wrap</param>
        /// <returns>A IMetahash instance</returns>
        public static IMetahash WrapMetadata(Dictionary<string, string> values, Options options)
        {
            return new Metahash(values, options);
        }

        /// <summary>
        /// Updates the options with settings from the data, if any
        /// </summary>
        /// <param name="db">The database to read from</param>
        /// <param name="options">The options to update</param>
        /// <param name="transaction">The transaction to use, if any</param>
        internal static void UpdateOptionsFromDb(LocalDatabase db, Options options, System.Data.IDbTransaction transaction = null)
        {
            string n = null;
            var opts = db.GetDbOptions(transaction);
            if (opts.ContainsKey("blocksize") && (!options.RawOptions.TryGetValue("blocksize", out n) || string.IsNullOrEmpty(n)))
                options.RawOptions["blocksize"] = opts["blocksize"] + "b";

            if (opts.ContainsKey("blockhash") && (!options.RawOptions.TryGetValue("block-hash-algorithm", out n) || string.IsNullOrEmpty(n)))
                options.RawOptions["block-hash-algorithm"] = opts["blockhash"];
            if (opts.ContainsKey("filehash") && (!options.RawOptions.TryGetValue("file-hash-algorithm", out n) || string.IsNullOrEmpty(n)))
                options.RawOptions["file-hash-algorithm"] = opts["filehash"];
        }

        /// <summary>
        /// Checks if the database contains options that need to be verified, such as the blocksize
        /// </summary>
        /// <param name="db">The database to check</param>
        /// <returns><c>true</c> if the database contains options that need to be verified; <c>false</c> otherwise</returns>
        internal static bool ContainsOptionsForVerification(LocalDatabase db)
        {
            var opts = db.GetDbOptions();
            return new[] { "blocksize", "blockhash", "filehash", "passphrase" }.Any(opts.ContainsKey);
        }

        /// <summary>
        /// Verifies the parameters in the database, and updates the database if needed
        /// </summary>
        /// <param name="db">The database to check</param>
        /// <param name="options">The options to verify</param>
        /// <param name="transaction">The transaction to use, if any</param>
        internal static void VerifyOptionsAndUpdateDatabase(LocalDatabase db, Options options, System.Data.IDbTransaction transaction = null)
        {
            var newDict = new Dictionary<string, string>
            {
                { "blocksize", options.Blocksize.ToString() },
                { "blockhash", options.BlockHashAlgorithm },
                { "filehash", options.FileHashAlgorithm }
            };
            var opts = db.GetDbOptions(transaction);

            if (options.NoEncryption)
            {
                newDict.Add("passphrase", "no-encryption");
            }
            else
            {
                string salt;
                opts.TryGetValue("passphrase-salt", out salt);
                if (string.IsNullOrEmpty(salt))
                {
                    // Not Crypto-class PRNG salts
                    var buf = new byte[32];
                    new Random().NextBytes(buf);
                    //Add version so we can detect and change the algorithm
                    salt = "v1:" + Library.Utility.Utility.ByteArrayAsHexString(buf);
                }

                newDict["passphrase-salt"] = salt;

                // We avoid storing the passphrase directly, 
                // instead we salt and rehash repeatedly
                newDict.Add("passphrase", Library.Utility.Utility.ByteArrayAsHexString(Library.Utility.Utility.RepeatedHashWithSalt(options.Passphrase, salt, 1200)));
            }


            var needsUpdate = false;
            foreach (var k in newDict)
                if (!opts.ContainsKey(k.Key))
                    needsUpdate = true;
                else if (opts[k.Key] != k.Value)
                {
                    if (k.Key == "passphrase")
                    {
                        if (!options.AllowPassphraseChange)
                        {
                            if (newDict[k.Key] == "no-encryption")
                                throw new Duplicati.Library.Interface.UserInformationException("You have attempted to remove the passphrase on an existing backup, which is not supported. Please configure a new clean backup if you want to remove the passphrase.", "PassphraseRemovalNotSupported");
                            else if (opts[k.Key] == "no-encryption")
                                throw new Duplicati.Library.Interface.UserInformationException("You have attempted to add a passphrase to an existing backup, which is not supported. Please configure a new clean backup if you want to add a passphrase.", "PassphraseAdditionNotSupported");
                            else
                                throw new Duplicati.Library.Interface.UserInformationException("You have attempted to change a passphrase to an existing backup, which is not supported. Please configure a new clean backup if you want to change the passphrase.", "PassphraseChangeNotSupported");
                        }
                    }
                    else
                        throw new Duplicati.Library.Interface.UserInformationException(string.Format("You have attempted to change the parameter \"{0}\" from \"{1}\" to \"{2}\", which is not supported. Please configure a new clean backup if you want to change the parameter.", k.Key, opts[k.Key], k.Value), "ParameterChangeNotSupported");

                }

            //Extra sanity check
            if (db.GetBlocksLargerThan(options.Blocksize) > 0)
                throw new Duplicati.Library.Interface.UserInformationException("You have attempted to change the block-size on an existing backup, which is not supported. Please configure a new clean backup if you want to change the block-size.", "BlockSizeChangeNotSupported");

            if (needsUpdate)
            {
                // Make sure we do not lose values
                foreach (var k in opts)
                    if (!newDict.ContainsKey(k.Key))
                        newDict[k.Key] = k.Value;

                db.SetDbOptions(newDict, transaction);
            }
        }
    }
}

