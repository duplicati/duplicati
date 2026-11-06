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


#nullable enable

using System.IO;
using System.Text.Json;

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// Reads and writes the pending install record in the updates folder
/// </summary>
public static class PendingInstallStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Gets the updates folder below the data folder, creating it if needed
    /// </summary>
    /// <param name="dataFolder">The data folder</param>
    /// <returns>The updates folder</returns>
    public static string GetUpdatesFolder(string dataFolder)
    {
        var folder = Path.Combine(dataFolder, UpdateHelperProtocol.UpdatesFolderName);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// The path of the record file in the updates folder
    /// </summary>
    public static string GetRecordPath(string updatesFolder)
        => Path.Combine(updatesFolder, UpdateHelperProtocol.PendingRecordFilename);

    /// <summary>
    /// Reads the record, or returns null if there is none
    /// </summary>
    public static PendingInstallRecord? Read(string updatesFolder)
    {
        var path = GetRecordPath(updatesFolder);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<PendingInstallRecord>(File.ReadAllText(path), Options);
    }

    /// <summary>
    /// Writes the record
    /// </summary>
    public static void Write(string updatesFolder, PendingInstallRecord record)
        => File.WriteAllText(GetRecordPath(updatesFolder), JsonSerializer.Serialize(record, Options));

    /// <summary>
    /// Deletes the record, if present
    /// </summary>
    public static void Delete(string updatesFolder)
    {
        var path = GetRecordPath(updatesFolder);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// Reads the result written by the helper, or returns null if there is none
    /// </summary>
    public static InstallResult? ReadResult(string resultFile)
    {
        if (string.IsNullOrWhiteSpace(resultFile) || !File.Exists(resultFile))
            return null;

        return JsonSerializer.Deserialize<InstallResult>(File.ReadAllText(resultFile), Options);
    }
}
