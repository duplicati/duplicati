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

internal static class InternxtBackend
{
    public static string Description => LC.L(@"This backend can read and write data to Internxt Drive using its REST protocol. Files are encrypted on the client with the account key, the same way the official Internxt clients do. Supported format is ""internxt://folder/subfolder"".");
    public static string DisplayName => LC.L(@"Internxt Drive");
    public static string TwoFactorShort => LC.L(@"Optional 2-factor code");
    public static string TwoFactorLong => LC.L(@"The 2-factor code to use for authentication, leave empty if the account is not MFA protected. Note that a new code must be provided by the user for each authentication attempt.");
    public static string TwoFactorSecretShort => LC.L(@"Optional 2-factor secret");
    public static string TwoFactorSecretLong => LC.L(@"The secret key the authenticator app was enrolled with (the text behind the QR code). When set, a fresh 2-factor code is computed for every login, so scheduled backups work on an MFA protected account. Storing it here means the second factor is kept together with the password.");
    public static string MoveToTrashShort => LC.L(@"Move to trash");
    public static string MoveToTrashLong => LC.L(@"If set, files will be moved to the trash instead of being deleted permanently.");
    public static string ApiUrlShort => LC.L(@"Internxt API URL");
    public static string ApiUrlLong(string defaultUrl) => LC.L(@"The base URL of the Internxt gateway. The ""/drive"" and ""/network"" paths are appended. Defaults to ""{0}"".", defaultUrl);
    public static string MultipartThresholdShort => LC.L(@"Multipart upload threshold");
    public static string MultipartThresholdLong => LC.L(@"Files larger than this size are uploaded in multiple parts. The Internxt network rejects multipart uploads below 100 MiB, so values below that are raised to 100 MiB.");
    public static string MultipartPartSizeShort => LC.L(@"Multipart part size");
    public static string MultipartPartSizeLong => LC.L(@"The size of each part in a multipart upload. Each part is held in memory while it is uploaded. Values below 5 MiB are raised to 5 MiB.");
    public static string ClientNameShort => LC.L(@"Client name header");
    public static string ClientNameLong => LC.L(@"The value sent in the ""internxt-client"" header. Internxt restricts API access by client and plan, so this can be changed if the default is rejected.");

    public static string UnknownAccount(string email) => LC.L(@"No Internxt account was found for ""{0}""", email);
    public static string TwoFactorRequired => LC.L(@"The Internxt account is protected by two-factor authentication. Supply the current code with the two-factor code option, or the authenticator secret with the two-factor secret option.");
    public static string TwoFactorSecretInvalid => LC.L(@"The two-factor secret is not a valid base32 authenticator key.");
    public static string LoginRejected => LC.L(@"Internxt rejected the login. Check the password and, if the account uses two-factor authentication, the code.");
    public static string PlanWithoutApiAccess(string message) => LC.L(@"The Internxt plan on this account does not include CLI/API access, so Duplicati cannot use it. The server said: {0}", message);
    public static string MnemonicDecryptFailed => LC.L(@"The account key could not be decrypted with this password. The password is wrong or the account key is corrupt.");
    public static string UnsupportedFileVersion(string name) => LC.L(@"The file ""{0}"" is stored in a format this backend cannot read", name);
    public static string ShardHashMismatch(string name) => LC.L(@"The downloaded data for ""{0}"" does not match the hash stored on the server", name);
    public static string FileTooLarge(string name, long size, long limit) => LC.L(@"The file ""{0}"" is {1} bytes, but the account allows at most {2} bytes per file. Lower the remote volume size to continue.", name, size, limit);
    public static string FolderNotFound(string path) => LC.L(@"The folder ""{0}"" was not found", path);
    public static string ApiError(int status, string body) => LC.L(@"The Internxt API returned {0}: {1}", status, body);
}
