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
namespace Duplicati.Library.Backend.AzureBlob.Strings {
    internal static class AzureBlobBackend {
        public static string Description_v2 { get { return LC.L(@"This backend can read and write data to Azure blob storage. Allowed format is ""azure://bucketname""."); } }
        public static string DisplayName { get { return LC.L(@"Azure blob"); } }
        public static string ContainerNameDescriptionLong { get { return LC.L(@"All files will be written to the container specified."); } }
        public static string ContainerNameDescriptionShort { get { return LC.L(@"The name of the storage container"); } }
        public static string NoStorageAccountName { get { return LC.L(@"No Azure storage account name given"); } }
        public static string StorageAccountNameDescriptionLong { get { return LC.L(@"The Azure storage account name which can be obtained by clicking the ""Manage Access Keys"" button on the storage account dashboard."); } }
        public static string StorageAccountNameDescriptionShort { get { return LC.L(@"The storage account name"); } }
        public static string AccessKeyDescriptionLong { get { return LC.L(@"The Azure access key which can be obtained by clicking the ""Manage Access Keys"" button on the storage account dashboard."); } }
        public static string AccessKeyDescriptionShort { get { return LC.L(@"The access key"); } }
        public static string SasTokenDescriptionLong { get { return LC.L(@"The Azure shared access signature (SAS) token which can be obtained by selecting the ""Shared access signature"" blade on the storage account dashboard, or inside a container blade."); } }
        public static string SasTokenDescriptionShort { get { return LC.L(@"The SAS token"); } }
        public static string NoAccessKeyOrSasToken { get { return LC.L(@"No Azure access key or SAS token given"); } }
        public static string AuthPasswordDescriptionLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string AuthPasswordDescriptionShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string AuthUsernameDescriptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string AuthUsernameDescriptionShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
    }
}
