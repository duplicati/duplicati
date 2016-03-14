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
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Text;
using Duplicati.Library.Main.Database;

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
            private readonly string m_hash;
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
                var hasher = System.Security.Cryptography.HashAlgorithm.Create(options.BlockHashAlgorithm);
                if (hasher == null)
                    throw new Exception(Strings.Common.InvalidHashAlgorithm(options.BlockHashAlgorithm));
                if (!hasher.CanReuseTransform)
                    throw new Exception(Strings.Common.InvalidCryptoSystem(options.BlockHashAlgorithm));
                    
                using (var ms = new System.IO.MemoryStream())
                using (var w = new StreamWriter(ms, Encoding.UTF8))
                {
                    w.Write(JsonConvert.SerializeObject(values));
                    w.Flush();
    
                    m_blob = ms.ToArray();
    
                    ms.Position = 0;
                    m_hash = Convert.ToBase64String(hasher.ComputeHash(ms));
                }
            }
    
            public string Hash
            {
                get { return m_hash; }
            }
    
            public long Size
            {
                get { return m_blob.Length; }
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

        internal static void UpdateOptionsFromDb(LocalDatabase db, Options options, System.Data.IDbTransaction transaction = null)
        {
            string n = null;
            var opts = db.GetDbOptions(transaction);
            if(opts.ContainsKey("blocksize") && (!options.RawOptions.TryGetValue("blocksize", out n) || string.IsNullOrEmpty(n)))
                options.RawOptions["blocksize"] = opts["blocksize"] + "b";

            if (opts.ContainsKey("blockhash") && (!options.RawOptions.TryGetValue("block-hash-algorithm", out n) || string.IsNullOrEmpty(n)))
                options.RawOptions["block-hash-algorithm"] = opts["blockhash"];
            if (opts.ContainsKey("filehash") && (!options.RawOptions.TryGetValue("file-hash-algorithm", out n) || string.IsNullOrEmpty(n)))
                options.RawOptions["file-hash-algorithm"] = opts["filehash"];
        }

        internal static void VerifyParameters(LocalDatabase db, Options options, System.Data.IDbTransaction transaction = null)
        {
            var newDict = new Dictionary<string, string>();
            newDict.Add("blocksize", options.Blocksize.ToString());
            newDict.Add("blockhash", options.BlockHashAlgorithm);
            newDict.Add("filehash", options.FileHashAlgorithm);
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
            foreach(var k in newDict)
                if (!opts.ContainsKey(k.Key))
                    needsUpdate = true;
                else if (opts[k.Key] != k.Value)
                {
                    if (k.Key == "passphrase")
                    {
                        if (!options.AllowPassphraseChange)
                        {
                            if (newDict[k.Key] == "no-encryption")
                                throw new Exception("Unsupported removal of passphrase");
                            else if (opts[k.Key] == "no-encryption")
                                throw new Exception("Unsupported addition of passphrase");
                            else
                                throw new Exception("Unsupported change of passphrase");
                        }
                    }
                    else
                        throw new Exception(string.Format("Unsupported change of parameter \"{0}\" from \"{1}\" to \"{2}\"", k.Key, opts[k.Key], k.Value));
                    
                }
                    
        
            //Extra sanity check
            if (db.GetBlocksLargerThan(options.Blocksize) > 0)
                throw new Exception("Unsupported block-size change detected");
        
            if (needsUpdate)
                db.SetDbOptions(newDict, transaction);               
        }

        /// <summary>
        /// The filename for the marker file that the user can add to suppress donation messages
        /// </summary>
        private const string SUPPRESS_DONATIONS_FILENAME = "suppress_donation_messages.txt";


        /// <summary>
        /// Gets or sets donation message suppression
        /// </summary>
        public static bool SuppressDonationMessages
        {
            get
            {
                try
                {
                    var folder = Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AutoUpdater.AutoUpdateSettings.AppName);
                    if (!Directory.Exists(folder))
                        Directory.CreateDirectory(folder);
                    
                    return File.Exists(Path.Combine(folder, SUPPRESS_DONATIONS_FILENAME));
                }
                catch
                {
                }

                return true;
            }
            set
            {
                try
                {
                    var folder = Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AutoUpdater.AutoUpdateSettings.AppName);
                    if (!Directory.Exists(folder))
                        Directory.CreateDirectory(folder);

                    var path = Path.Combine(folder, SUPPRESS_DONATIONS_FILENAME);

                    if (value)
                        using(File.OpenWrite(path))
                        {
                        }
                    else
                        File.Delete(path);
                        
                }
                catch
                {
                }
            }
        }
    }
}

