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

namespace Duplicati.Library.Backend.AzureBlob.Strings;

internal static class AzureBlobBackend {
    public static string DescriptionV2 => LC.L(@"This backend can read and write data to Azure blob storage. Allowed format is ""azure://bucketname"".");
    public static string DisplayName => LC.L(@"Azure blob");
    public static string ContainerNameDescriptionLong => LC.L(@"All files will be written to the container specified.");
    public static string ContainerNameDescriptionShort => LC.L(@"The name of the storage container");
    public static string NoStorageAccountName => LC.L(@"No Azure storage account name given");
    public static string StorageAccountNameDescriptionLong => LC.L(@"The Azure storage account name which can be obtained by clicking the ""Manage Access Keys"" button on the storage account dashboard.");
    public static string StorageAccountNameDescriptionShort => LC.L(@"The storage account name");
    public static string AccessKeyDescriptionLong => LC.L(@"The Azure access key which can be obtained by clicking the ""Manage Access Keys"" button on the storage account dashboard.");
    public static string AccessKeyDescriptionShort => LC.L(@"The access key");
    public static string SasTokenDescriptionLong => LC.L(@"The Azure shared access signature (SAS) token which can be obtained by selecting the ""Shared access signature"" blade on the storage account dashboard, or inside a container blade.");
    public static string SasTokenDescriptionShort => LC.L(@"The SAS token");
    public static string NoAccessKeyOrSasToken => LC.L(@"No Azure access key or SAS token given");
    public static string AuthPasswordDescriptionLong => LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD"".");
    public static string AuthPasswordDescriptionShort => LC.L(@"Supply the password used to connect to the server");
    public static string AuthUsernameDescriptionLong => LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME"".");
    public static string AuthUsernameDescriptionShort => LC.L(@"Supply the username used to connect to the server");
}