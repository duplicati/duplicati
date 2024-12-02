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
using System.Diagnostics;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that decrypts the volumes that the `VolumeDownloader` process has downloaded.
    /// </summary>
    internal class VolumeDecrypter
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDecrypter>();

        public static Task Run(Options options, RestoreResults results)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.downloadedVolume.ForRead,
                Output = Channels.decryptedVolume.ForWrite
            },
            async self =>
            {
                Stopwatch sw_read    = options.InternalProfiling ? new () : null;
                Stopwatch sw_write   = options.InternalProfiling ? new () : null;
                Stopwatch sw_decrypt = options.InternalProfiling ? new () : null;
                try
                {
                    while (true)
                    {
                        // Get the block request and volume from the `VolumeDownloader` process.
                        sw_read?.Start();
                        var (block_request, volume) = await self.Input.ReadAsync();
                        sw_read?.Stop();

                        sw_decrypt?.Start();
                        // Trigger the download, which will also decrypt the volume.
                        TempFile f = null;
                        try
                        {
                            f = volume.Wait();
                        }
                        catch (Exception)
                        {
                            lock (results)
                            {
                                results.BrokenRemoteFiles.Add(block_request.VolumeID);
                            }
                            throw;
                        }
                        sw_decrypt?.Stop();

                        sw_write?.Start();
                        // Pass the decrypted volume to the `VolumeDecompressor` process.
                        await self.Output.WriteAsync((block_request, f));
                        sw_write?.Stop();
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume decrypter retired");

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Read: {sw_read.ElapsedMilliseconds}ms, Decrypt: {sw_decrypt.ElapsedMilliseconds}ms, Write: {sw_write.ElapsedMilliseconds}ms");
                        Console.WriteLine($"Volume decryptor - Read: {sw_read.ElapsedMilliseconds}ms, Decrypt: {sw_decrypt.ElapsedMilliseconds}ms, Write: {sw_write.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "DecryptionError", ex, "Error during decryption");
                    throw;
                }
            });
        }
    }

}