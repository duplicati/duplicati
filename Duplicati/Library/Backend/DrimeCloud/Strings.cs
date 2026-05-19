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

using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings;

/// <summary>
/// Localization strings for Drime Cloud backend
/// </summary>
internal static class DrimeCloud
{
    /// <summary>
    /// Backend description
    /// </summary>
    public static string Description { get { return LC.L(@"This backend can read and write data to Drime Cloud. Allowed formats are ""drimecloud://folder"" and ""drimecloud://username:password@folder""."); } }

    /// <summary>
    /// Display name for the backend
    /// </summary>
    public static string DisplayName { get { return LC.L(@"Drime Cloud"); } }

    /// <summary>
    /// Short description for API token option
    /// </summary>
    public static string DescriptionApiTokenShort { get { return LC.L(@"Drime Cloud API token"); } }

    /// <summary>
    /// Long description for API token option
    /// </summary>
    public static string DescriptionApiTokenLong { get { return LC.L(@"Supply the Drime Cloud API token instead of the username and password. Can be obtained from: ""https://app.drime.cloud/account-settings#developers"""); } }

    /// <summary>
    /// Short description for API URL option
    /// </summary>
    public static string DescriptionApiUrlShort { get { return LC.L(@"Drime Cloud API URL"); } }

    /// <summary>
    /// Long description for API URL option
    /// </summary>
    public static string DescriptionApiUrlLong { get { return LC.L(@"Set the Drime Cloud API URL if using a non-standard URL. Defaults to ""https://app.drime.cloud/api/v1"""); } }

    /// <summary>
    /// Short description for page size option
    /// </summary>
    public static string DescriptionPageSizeShort { get { return LC.L(@"Drime Cloud page size"); } }

    /// <summary>
    /// Long description for page size option
    /// </summary>
    public static string DescriptionPageSizeLong { get { return LC.L(@"Adjusts the Drime Cloud API page size. Defaults to 50."); } }

    /// <summary>
    /// Short description for workspace ID option
    /// </summary>
    public static string DescriptionWorkspaceIdShort { get { return LC.L(@"Drime Cloud workspace ID"); } }

    /// <summary>
    /// Long description for workspace ID option
    /// </summary>
    public static string DescriptionWorkspaceIdLong { get { return LC.L(@"Set the Drime Cloud workspace ID to use. Use 0 for personal workspace (default)."); } }

    /// <summary>
    /// Short description for soft delete option
    /// </summary>
    public static string DescriptionSoftDeleteShort { get { return LC.L(@"Use soft delete"); } }

    /// <summary>
    /// Long description for soft delete option
    /// </summary>
    public static string DescriptionSoftDeleteLong { get { return LC.L(@"When enabled, deleted files are moved to trash instead of being permanently deleted."); } }

    /// <summary>
    /// Error message for missing credentials
    /// </summary>
    public static string MissingCredentialsError { get { return LC.L(@"Either an API token or username/password must be provided for Drime Cloud authentication."); } }

    /// <summary>
    /// Error message for authentication failure
    /// </summary>
    public static string AuthenticationFailedError(int statusCode, string message) { return LC.L(@"Failed to authenticate with Drime Cloud: {0}-{1}", statusCode, message); }

    /// <summary>
    /// Error message for banned user
    /// </summary>
    public static string UserBannedError(string bannedAt) { return LC.L(@"User is banned from Drime Cloud since: {0}", bannedAt); }

    /// <summary>
    /// Error message for folder not found
    /// </summary>
    public static string FolderNotFoundError(string folderName) { return LC.L(@"Folder not found: {0}", folderName); }

    /// <summary>
    /// Error message for upload failure
    /// </summary>
    public static string UploadFailedError(string message) { return LC.L(@"Failed to upload file to Drime Cloud: {0}", message); }

    /// <summary>
    /// Error message for invalid page size
    /// </summary>
    public static string InvalidPageSizeError(string option, int value) { return LC.L(@"Invalid page size value for option {0}: {1}", option, value); }
}
