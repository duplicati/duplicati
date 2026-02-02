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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This class encapsulates the generation of metadata for a filesystem entry
    /// </summary>
    internal static class MetadataGenerator
    {
        private static readonly string METALOGTAG = Logging.Log.LogTagFromType(typeof(MetadataGenerator)) + ".Metadata";

        public static async Task<Dictionary<string, string>> GenerateMetadata(ISourceProviderEntry entry, System.IO.FileAttributes attributes, Options options, CancellationToken token)
        {
            try
            {
                Dictionary<string, string> metadata;

                if (!options.SkipMetadata)
                {
                    metadata = await entry.GetMinorMetadata(token).ConfigureAwait(false);
                    if (metadata == null)
                        metadata = new Dictionary<string, string>();

                    if (!metadata.ContainsKey("CoreAttributes"))
                        metadata["CoreAttributes"] = attributes.ToString();

                    if (!metadata.ContainsKey("CoreLastWritetime"))
                    {
                        try
                        {
                            metadata["CoreLastWritetime"] = entry.LastModificationUtc.Ticks.ToString();
                        }
                        catch (Exception ex)
                        {
                            LogExceptionHelper.LogCommonWarning(ex, METALOGTAG, "TimestampReadFailed", entry.Path, "Failed to read timestamp on \"{0}\"");
                        }
                    }

                    if (!metadata.ContainsKey("CoreCreatetime"))
                    {
                        try
                        {
                            metadata["CoreCreatetime"] = entry.CreatedUtc.Ticks.ToString();
                        }
                        catch (Exception ex)
                        {
                            LogExceptionHelper.LogCommonWarning(ex, METALOGTAG, "TimestampReadFailed", entry.Path, "Failed to read timestamp on \"{0}\"");
                        }
                    }
                }
                else
                {
                    metadata = new Dictionary<string, string>();
                }

                return metadata;
            }
            catch (Exception ex)
            {
                LogExceptionHelper.LogCommonWarning(ex, METALOGTAG, "MetadataProcessFailed", entry.Path, "Failed to process metadata on \"{0}\", storing empty metadata");
                return new Dictionary<string, string>();
            }
        }
    }
}

