// Copyright (C) 2026, The Duplicati Team
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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;
using System.Threading;

namespace Duplicati.Library.Main
{
    public class Utility
    {
        /// <summary>
        /// The sentinel value used to indicate that encryption is disabled
        /// </summary>
        private const string NoEncryptionSentinel = "no-encryption";

        /// <summary>
        /// The mapping between the options and the database keys
        /// </summary>
        /// <remarks>
        /// The tuple contains the following elements:
        /// 1. The key used in the database
        /// 2. The key used in the options
        /// 3. A function to read the value from the parsed options
        /// 4. A function to write the value to the parsed options
        /// </remarks>
        private static readonly (string DatabaseKey, string OptionKey, Func<Options, string> GetValue, Func<string, string> RestoreValue)[] PersistedOptionMappings =
        [
            ("prefix", "prefix", options => options.Prefix, value => value),
            ("blocksize", "blocksize", options => options.Blocksize.ToString(), value => value + "b"),
            ("blockhash", "block-hash-algorithm", options => options.BlockHashAlgorithm, value => value),
            ("filehash", "file-hash-algorithm", options => options.FileHashAlgorithm, value => value),
            ("dblock-size", "dblock-size", options => options.VolumeSize.ToString(), value => value + "b"),
            ("compression-module", "compression-module", options => options.CompressionModule, value => value),
            ("encryption-module", "encryption-module", options => options.NoEncryption ? null : options.EncryptionModule, value => value)
        ];

        /// <summary>
        /// Keys that are persisted in the database
        /// </summary>
        private static readonly string[] PersistedOptionKeys =
        [
            .. PersistedOptionMappings.Select(option => option.DatabaseKey),
            "passphrase",
            "passphrase-salt"
        ];

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

            /// <summary>
            /// Constructs a new metahash instance
            /// </summary>
            /// <param name="values">The values to include in the metahash</param>
            /// <param name="options">The options to use for hashing</param>
            public Metahash(Dictionary<string, string> values, Options options)
            {
                m_values = values;

                using (var ms = new MemoryStream())
                using (var w = new StreamWriter(ms, Encoding.UTF8))
                using (var filehasher = HashFactory.CreateHasher(options.FileHashAlgorithm))
                {
                    if (filehasher == null)
                        throw new Interface.UserInformationException(Strings.Common.InvalidHashAlgorithm(options.FileHashAlgorithm), "FileHashAlgorithmNotSupported");

                    w.Write(JsonConvert.SerializeObject(values));
                    w.Flush();

                    m_blob = ms.ToArray();

                    ms.Position = 0;
                    m_filehash = Convert.ToBase64String(filehasher.ComputeHash(ms));
                }
            }

            /// <inheritdoc />
            public string FileHash => m_filehash;

            /// <inheritdoc />
            public byte[] Blob => m_blob;

            /// <inheritdoc />
            public Dictionary<string, string> Values => m_values;
        }

        /// <summary>
        /// Constructs a container for a given metadata dictionary
        /// </summary>
        /// <param name="values">The metadata values to wrap</param>
        /// <returns>A IMetahash instance</returns>
        public static IMetahash WrapMetadata(Dictionary<string, string> values, Options options)
            => new Metahash(values, options);

        /// <summary>
        /// Updates the options with settings from the data, if any.
        /// </summary>
        /// <param name="db">The database to read from.</param>
        /// <param name="options">The options to update.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the options have been updated.</returns>
        internal static async Task UpdateOptionsFromDb(LocalDatabase db, Options options, CancellationToken cancellationToken)
        {
            var opts = await db.GetDbOptions(cancellationToken).ConfigureAwait(false);

            foreach (var option in PersistedOptionMappings)
                RestoreOptionFromDb(opts, options, option.DatabaseKey, option.OptionKey, option.RestoreValue);

            if (opts.TryGetValue("passphrase", out var passphraseState)
                && passphraseState == NoEncryptionSentinel
                && !HasExplicitOptionValue(options, "no-encryption")
                && !HasExplicitOptionValue(options, "passphrase"))
                options.RawOptions["no-encryption"] = "true";
        }

        /// <summary>
        /// Checks if the database contains options that need to be verified, such as the blocksize.
        /// </summary>
        /// <param name="db">The database to check.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited returns <c>true</c> if the database contains options that need to be verified; <c>false</c> otherwise.</returns>
        internal static async Task<bool> ContainsOptionsForVerification(LocalDatabase db, CancellationToken cancellationToken)
        {
            var opts = await db.GetDbOptions(cancellationToken).ConfigureAwait(false);
            await db.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return PersistedOptionMappings
                .Select(option => option.DatabaseKey)
                .Concat(["passphrase"])
                .Any(opts.ContainsKey);
        }

        /// <summary>
        /// Verifies the parameters in the database, and updates the database if needed.
        /// </summary>
        /// <param name="db">The database to check.</param>
        /// <param name="options">The options to verify.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the options have been verified and the database has been updated if needed.</returns>
        internal static async Task VerifyOptionsAndUpdateDatabase(LocalDatabase db, Options options, CancellationToken cancellationToken)
        {
            var newDict = GetPersistedOptionValues(options);
            var opts = await db.GetDbOptions(cancellationToken).ConfigureAwait(false);

            if (options.NoEncryption)
            {
                newDict["passphrase"] = NoEncryptionSentinel;
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
                newDict["passphrase"] = Library.Utility.Utility.ByteArrayAsHexString(Library.Utility.Utility.RepeatedHashWithSalt(options.Passphrase, salt, 1200));
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
                            if (newDict[k.Key] == NoEncryptionSentinel)
                                throw new Duplicati.Library.Interface.UserInformationException("You have attempted to remove the passphrase on an existing backup, which is not supported. Please configure a new clean backup if you want to remove the passphrase.", "PassphraseRemovalNotSupported");
                            else if (opts[k.Key] == NoEncryptionSentinel)
                                throw new Duplicati.Library.Interface.UserInformationException("You have attempted to add a passphrase to an existing backup, which is not supported. Please configure a new clean backup if you want to add a passphrase.", "PassphraseAdditionNotSupported");
                            else
                                throw new Duplicati.Library.Interface.UserInformationException("You have attempted to change a passphrase to an existing backup, which is not supported. Please configure a new clean backup if you want to change the passphrase.", "PassphraseChangeNotSupported");
                        }
                    }
                    else
                        throw new Duplicati.Library.Interface.UserInformationException(string.Format("You have attempted to change the parameter \"{0}\" from \"{1}\" to \"{2}\", which is not supported. Please configure a new clean backup if you want to change the parameter.", k.Key, opts[k.Key], k.Value), "ParameterChangeNotSupported");

                }

            //Extra sanity check
            if (await db.GetBlocksLargerThan(options.Blocksize, cancellationToken).ConfigureAwait(false) > 0)
                throw new Duplicati.Library.Interface.UserInformationException("You have attempted to change the block-size on an existing backup, which is not supported. Please configure a new clean backup if you want to change the block-size.", "BlockSizeChangeNotSupported");

            if (needsUpdate)
                await PersistOptionsToDb(db, options, opts, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Persists the current write-path options to the local database and removes stale persisted values.
        /// </summary>
        /// <param name="dbpath">The path to the local database.</param>
        /// <param name="options">The options to persist.</param>
        /// <param name="operation">The operation description to record in the database.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the options have been stored.</returns>
        public static async Task PersistOptionsToDatabaseWithoutValidation(string dbpath, Options options, string operation, CancellationToken cancellationToken)
        {
            await using var db = await LocalDatabase.CreateLocalDatabaseAsync(dbpath, operation, true, null, cancellationToken).ConfigureAwait(false);
            await PersistOptionsToDb(db, options, null, cancellationToken).ConfigureAwait(false);
            await db.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Restores persisted write-path options from the local database into the supplied options instance.
        /// Explicit CLI/raw options continue to take precedence over stored values.
        /// </summary>
        /// <param name="dbpath">The path to the local database.</param>
        /// <param name="options">The options to update.</param>
        /// <param name="operation">The operation description to record in the database.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the options have been restored.</returns>
        public static async Task UpdateOptionsFromDatabase(string dbpath, Options options, string operation, CancellationToken cancellationToken)
        {
            await using var db = await LocalDatabase.CreateLocalDatabaseAsync(dbpath, operation, true, null, cancellationToken).ConfigureAwait(false);
            await UpdateOptionsFromDb(db, options, cancellationToken).ConfigureAwait(false);
            await db.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the options from the given options set and returns them in a dictionary with the database keys as keys.
        /// </summary>
        /// <param name="options">The parsed options to translate</param>
        /// <returns>A dictionary with the database keys as keys</returns>
        private static Dictionary<string, string> GetPersistedOptionValues(Options options)
        {
            var result = new Dictionary<string, string>();

            foreach (var option in PersistedOptionMappings)
            {
                var value = option.GetValue(options);
                if (!string.IsNullOrWhiteSpace(value))
                    result[option.DatabaseKey] = value;
            }

            return result;
        }

        /// <summary>
        /// Updates the options with settings from the data, if any.
        /// </summary>
        /// <param name="db">The database to update</param>
        /// <param name="options">The parsed options to update</param>
        /// <param name="existingOptions">The existing options to merge with, or null</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>An awaitable task</returns>
        private static async Task PersistOptionsToDb(LocalDatabase db, Options options, IDictionary<string, string> existingOptions, CancellationToken cancellationToken)
        {
            existingOptions ??= await db.GetDbOptions(cancellationToken).ConfigureAwait(false);
            await db.SetDbOptions(BuildStoredOptions(existingOptions, options), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds a dictionary of options to store in the database, merging existing options with the new ones.
        /// </summary>
        /// <param name="existingOptions">The existing options to merge with</param>
        /// <param name="options">The parsed options</param>
        /// <returns>A dictionary with the database keys as keys</returns>
        private static Dictionary<string, string> BuildStoredOptions(IDictionary<string, string> existingOptions, Options options)
        {
            var result = existingOptions
                .Where(entry => !PersistedOptionKeys.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

            foreach (var entry in GetPersistedOptionValues(options))
                result[entry.Key] = entry.Value;

            if (options.NoEncryption)
            {
                result["passphrase"] = NoEncryptionSentinel;
            }
            else
            {
                existingOptions.TryGetValue("passphrase-salt", out var salt);
                if (string.IsNullOrEmpty(salt))
                {
                    var buf = new byte[32];
                    new Random().NextBytes(buf);
                    salt = "v1:" + Library.Utility.Utility.ByteArrayAsHexString(buf);
                }

                result["passphrase-salt"] = salt;
                result["passphrase"] = Library.Utility.Utility.ByteArrayAsHexString(Library.Utility.Utility.RepeatedHashWithSalt(options.Passphrase, salt, 1200));
            }

            return result;
        }

        /// <summary>
        /// Restores an option from the database if it is not already set in the options.
        /// </summary>
        /// <param name="persistedOptions">The options stored in the database</param>
        /// <param name="options">The parsed options to update</param>
        /// <param name="databaseKey">The database key to look for</param>
        /// <param name="optionKey">The option key to set</param>
        /// <param name="restoreValue">The value to restore</param>
        private static void RestoreOptionFromDb(IDictionary<string, string> persistedOptions, Options options, string databaseKey, string optionKey, Func<string, string> restoreValue)
        {
            if (persistedOptions.TryGetValue(databaseKey, out var value) && !string.IsNullOrWhiteSpace(value) && !HasExplicitOptionValue(options, optionKey))
                options.RawOptions[optionKey] = restoreValue(value);
        }

        /// <summary>
        /// Checks if the given option key has an explicit value in the options.
        /// </summary>
        /// <param name="options">The parsed options</param>
        /// <param name="optionKey">The option key to check</param>
        /// <returns>True if the option has an explicit value, false otherwise</returns>
        private static bool HasExplicitOptionValue(Options options, string optionKey)
            => options.RawOptions.TryGetValue(optionKey, out var value) && !string.IsNullOrWhiteSpace(value);
    }
}

