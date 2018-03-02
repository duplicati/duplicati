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
using System.Threading.Tasks;
using System.Collections.Generic;
using CoCoL;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This class encasuplates the generation of metadata for a filesystem entry
    /// </summary>
    internal static class MetadataGenerator
    {
        public static async Task<Dictionary<string, string>> GenerateMetadataAsync(string path, System.IO.FileAttributes attributes, Options options, Snapshots.ISnapshotService snapshot, LogWrapper log)
        {
            try
            {
                Dictionary<string, string> metadata;

                if (options.StoreMetadata)
                {
                    metadata = snapshot.GetMetadata(path, snapshot.IsSymlink(path, attributes), options.SymlinkPolicy == Options.SymlinkStrategy.Follow);
                    if (metadata == null)
                        metadata = new Dictionary<string, string>();

                    if (!metadata.ContainsKey("CoreAttributes"))
                        metadata["CoreAttributes"] = attributes.ToString();

                    if (!metadata.ContainsKey("CoreLastWritetime"))
                    {
                        try
                        {
                            metadata["CoreLastWritetime"] = snapshot.GetLastWriteTimeUtc(path).Ticks.ToString();
                        }
                        catch (Exception ex)
                        {
                            await log.WriteWarningAsync(string.Format("Failed to read timestamp on \"{0}\"", path), ex);
                        }
                    }

                    if (!metadata.ContainsKey("CoreCreatetime"))
                    {
                        try
                        {
                            metadata["CoreCreatetime"] = snapshot.GetCreationTimeUtc(path).Ticks.ToString();
                        }
                        catch (Exception ex)
                        {
                            await log.WriteWarningAsync(string.Format("Failed to read timestamp on \"{0}\"", path), ex);
                        }
                    }
                }
                else
                {
                    metadata = new Dictionary<string, string>();
                }

                return metadata;
            }
            catch(Exception ex)
            {
                await log.WriteWarningAsync(string.Format("Failed to process metadata for \"{0}\", storing empty metadata", path), ex);
                return new Dictionary<string, string>();
            }
        }
    }
}

