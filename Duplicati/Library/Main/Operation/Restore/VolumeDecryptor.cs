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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that decrypts the volumes that the `VolumeDownloader` process has downloaded.
    /// </summary>
    internal class VolumeDecryptor
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDecryptor>();

        /// <summary>
        /// Runs the volume decryptor process.
        /// </summary>
        /// <param name="options">The restore options.</param>
        public static Task Run(Channels channels, Options options)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.DecryptRequest.AsRead(),
                Output = channels.VolumeRequestResponse.AsWrite()
            },
            async self =>
            {
                Stopwatch sw_read = options.InternalProfiling ? new() : null;
                Stopwatch sw_write = options.InternalProfiling ? new() : null;
                Stopwatch sw_decrypt = options.InternalProfiling ? new() : null;
                try
                {
                    // Taken from BackendManager.GetOperation
                    using var encryption = options.NoEncryption
                                    ? null
                                    : (DynamicLoader.EncryptionLoader.GetModule(options.EncryptionModule, options.Passphrase, options.RawOptions)
                                        ?? throw new Exception(Strings.BackendMananger.EncryptionModuleNotFound(options.EncryptionModule))
                                );

                    while (true)
                    {
                        // Get the block request and volume from the `VolumeDownloader` process.
                        sw_read?.Start();
                        var (volume_id, volume_name, volume) = await self.Input.ReadAsync().ConfigureAwait(false);
                        sw_read?.Stop();

                        sw_decrypt?.Start();
                        var tmpfile = DecryptFile(volume, DetectEncryptionModule(volume_name, options, encryption));
                        var bvr = new BlockVolumeReader(options.CompressionModule, tmpfile, options);
                        sw_decrypt?.Stop();

                        sw_write?.Start();
                        // Pass the decrypted volume to the `VolumeDecompressor` process.
                        await self.Output.WriteAsync((volume_id, volume, bvr)).ConfigureAwait(false);
                        sw_write?.Stop();
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume decryptor retired");

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Read: {sw_read.ElapsedMilliseconds}ms, Decrypt: {sw_decrypt.ElapsedMilliseconds}ms, Write: {sw_write.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "DecryptionError", ex, "Error during decryption");
                    throw;
                }
            });
        }

        // Taken from BackendManager.GetOperation
        public static Library.Utility.TempFile DecryptFile(Library.Utility.TempFile tempFile, Interface.IEncryption? decrypter)
        {
            // Support no encryption
            if (decrypter == null)
                return tempFile;

            Library.Utility.TempFile? decryptTarget = null;

            // Always dispose the source file
            using (tempFile)
            using (new Logging.Timer(LOGTAG, "DecryptFile", "Decrypting " + tempFile))
            {
                try
                {
                    decryptTarget = new Library.Utility.TempFile();
                    try { decrypter.Decrypt(tempFile, decryptTarget); }
                    // If we fail here, make sure that we throw a crypto exception
                    catch (System.Security.Cryptography.CryptographicException) { throw; }
                    catch (Exception ex) { throw new System.Security.Cryptography.CryptographicException(ex.Message, ex); }

                    var result = decryptTarget;
                    decryptTarget = null;
                    return result;
                }
                finally
                {
                    // Remove temp files on failure
                    decryptTarget?.Dispose();
                }
            }
        }

        private static Interface.IEncryption? DetectEncryptionModule(string remotefilename, Options options, Interface.IEncryption? encryption)
        {
            try
            {
                // Auto-guess the encryption module
                var ext = (System.IO.Path.GetExtension(remotefilename) ?? "").TrimStart('.');
                if (!ext.Equals(encryption?.FilenameExtension, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if the file is not encrypted
                    if (DynamicLoader.CompressionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        if (encryption != null)
                            Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", guessing that it is not encrypted", ext, options.EncryptionModule);
                        return null;
                    }
                    // Check if the file is encrypted with something else
                    else if (DynamicLoader.EncryptionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", attempting to use matching encryption module", ext, options.EncryptionModule);

                        try
                        {
                            return DynamicLoader.EncryptionLoader.GetModule(ext, options.Passphrase, options.RawOptions)
                                ?? encryption;
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "AutomaticDecryptionDetection", ex, "Failed to load encryption module \"{0}\", using specified encryption module \"{1}\"", ext, options.EncryptionModule);
                        }
                    }
                    // Fallback, lets see what happens...
                    else
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", attempting to use specified encryption module as no others match", ext, options.EncryptionModule);
                    }
                }

                return encryption;
            }
            // If we fail here, make sure that we throw a crypto exception
            catch (System.Security.Cryptography.CryptographicException) { throw; }
            catch (Exception ex) { throw new System.Security.Cryptography.CryptographicException(ex.Message, ex); }
        }
    }

}