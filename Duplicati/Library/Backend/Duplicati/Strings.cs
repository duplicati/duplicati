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

using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Duplicati.Strings;

internal static class DuplicatiBackend
{
    public static string Description { get { return LC.L(@"This backend can read and write data to Duplicati Storage."); } }
    public static string DisplayName { get { return LC.L(@"Duplicati Storage Backend"); } }

    public static string AuthIdOptionsShort { get { return LC.L(@"The API ID for authenticating with the Duplicati storage server."); } }
    public static string AuthIdOptionsLong { get { return LC.L(@"Specifies the API ID to use when authenticating with the Duplicati storage server."); } }
    public static string AuthKeyOptionsShort { get { return LC.L(@"The API Key for authenticating with the Duplicati storage server."); } }
    public static string AuthKeyOptionsLong { get { return LC.L(@"Specifies the API Key to use when authenticating with the Duplicati storage server."); } }
    public static string BackupIdOptionsShort { get { return LC.L(@"The Backup ID to use on the Duplicati storage server."); } }
    public static string BackupIdOptionsLong { get { return LC.L(@"Each backupID identifies a separate backup set on the Duplicati storage server."); } }
    public static string ErrorMissingBackupId { get { return LC.L(@"A unique backup id must be specified"); } }
}

internal static class ListFoldersModule
{
    public static string Description { get { return LC.L(@"Lists the backup folders on a Duplicati storage server."); } }
    public static string DisplayName { get { return LC.L(@"List Duplicati Backup Folders"); } }

    public static string ActionDescriptionShort { get { return LC.L(@"The action to perform."); } }
    public static string ActionDescriptionLong { get { return LC.L(@"Specifies the action to perform."); } }

    public static string UrlDescriptionShort { get { return LC.L(@"The URL of the Duplicati storage server."); } }
    public static string UrlDescriptionLong { get { return LC.L(@"Specifies the URL of the Duplicati storage server."); } }
}