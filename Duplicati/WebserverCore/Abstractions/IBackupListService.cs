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

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Provides methods for handling stored backups
/// </summary>
public interface IBackupListService
{
    IEnumerable<Dto.BackupAndScheduleOutputDto> List(string? orderBy);

    /// <summary>
    /// Imports a backup configuration
    /// </summary>
    /// <param name="global_options">What to do with the global advanced options in the configuration: "ignore" (or null), "merge" into the backup, or "apply" as global options of this server (only with direct)</param>
    Dto.ImportBackupOutputDto Import(bool cmdline, bool import_metadata, bool direct, bool temporary, string passphrase, string tempfile, Dictionary<string, string>? replace_settings, bool expand_env_vars = false, string? global_options = null);

    Dto.CreateBackupDto Add(Dto.BackupAndScheduleInputDto data, bool temporary, bool existingDb);
}