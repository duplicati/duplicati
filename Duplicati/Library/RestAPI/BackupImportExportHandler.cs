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
using System.Linq;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.Library.RestAPI;

public static class BackupImportExportHandler
{
    public static void RemovePasswords(IBackup backup)
    {
        backup.SanitizeSettings();
        backup.SanitizeTargetUrl();
    }

    public static byte[] ExportToJSON(Connection connection, IBackup backup, string passphrase)
    {
        Server.Serializable.ImportExportStructure ipx = connection.PrepareBackupForExport(backup);

        byte[] data;
        using (var ms = new System.IO.MemoryStream())
        {
            using (var sw = new System.IO.StreamWriter(ms))
            {
                Serializer.SerializeJson(sw, ipx, true);

                if (!string.IsNullOrWhiteSpace(passphrase))
                {
                    ms.Position = 0;
                    using (var ms2 = new System.IO.MemoryStream())
                    {
                        using (var m = new Duplicati.Library.Encryption.AESEncryption(passphrase, new Dictionary<string, string>()))
                        {
                            m.Encrypt(ms, ms2);
                            data = ms2.ToArray();
                        }
                    }
                }
                else
                {
                    data = ms.ToArray();
                }
            }
        }

        return data;
    }

    public static Server.Serializable.ImportExportStructure ImportBackup(Connection connection, string configurationFile, bool importMetadata, Func<string> getPassword)
    {
        // This removes the ID and DBPath from the backup configuration.
        Server.Serializable.ImportExportStructure importedStructure = LoadConfiguration(configurationFile, importMetadata, getPassword);

        if (connection.Backups.Any(x => x.Name.Equals(importedStructure.Backup.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A backup with the name {importedStructure.Backup.Name} already exists.");
        }

        string error = connection.ValidateBackup(importedStructure.Backup, importedStructure.Schedule);
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException(error);
        }

        // This creates a new ID and DBPath.
        connection.AddOrUpdateBackupAndSchedule(importedStructure.Backup, importedStructure.Schedule);

        return importedStructure;
    }

    public static Server.Serializable.ImportExportStructure LoadConfiguration(string filename, bool importMetadata, Func<string> getPassword)
    {
        Server.Serializable.ImportExportStructure ipx;

        var buf = new byte[3];
        using (var fs = System.IO.File.OpenRead(filename))
        {
            Duplicati.Library.Utility.Utility.ForceStreamRead(fs, buf, buf.Length);

            fs.Position = 0;
            if (buf[0] == 'A' && buf[1] == 'E' && buf[2] == 'S')
            {
                using (var m = new Duplicati.Library.Encryption.AESEncryption(getPassword(), new Dictionary<string, string>()))
                {
                    using (var m2 = m.Decrypt(fs))
                    {
                        using (var sr = new System.IO.StreamReader(m2))
                        {
                            ipx = Serializer.Deserialize<Server.Serializable.ImportExportStructure>(sr);
                        }
                    }
                }
            }
            else
            {
                using (var sr = new System.IO.StreamReader(fs))
                {
                    ipx = Serializer.Deserialize<Server.Serializable.ImportExportStructure>(sr);
                }
            }
        }

        if (ipx.Backup == null)
        {
            throw new Exception("No backup found in document");
        }

        if (ipx.Backup.Metadata == null)
        {
            ipx.Backup.Metadata = new Dictionary<string, string>();
        }

        if (!importMetadata)
        {
            ipx.Backup.Metadata.Clear();
        }

        ipx.Backup.ID = null;
        ipx.Backup.DBPath = null;

        if (ipx.Schedule != null)
        {
            ipx.Schedule.ID = -1;
        }

        return ipx;
    }
}
