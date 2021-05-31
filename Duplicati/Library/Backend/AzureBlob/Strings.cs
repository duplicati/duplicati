using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.AzureBlob.Strings {
    internal static class AzureBlobBackend {
        public static string ContainerNameDescriptionLong { get { return LC.L(@"All files will be written to the container specified"); } }
        public static string ContainerNameDescriptionShort { get { return LC.L(@"The name of the storage container "); } }
        public static string DisplayName { get { return LC.L(@"Azure blob"); } }
        public static string NoStorageAccountName { get { return LC.L(@"No Azure storage account name given"); } }
        public static string StorageAccountNameDescriptionLong { get { return LC.L(@"The Azure storage account name which can be obtained by clicking the ""Manage Access Keys"" button on the storage account dashboard"); } }
        public static string StorageAccountNameDescriptionShort { get { return LC.L(@"The storage account name"); } }
        public static string AccessKeyDescriptionLong { get { return LC.L(@"The Azure access key which can be obtained by clicking the ""Manage Access Keys"" button on the storage account dashboard"); } }
        public static string AccessKeyDescriptionShort { get { return LC.L(@"The access key"); } }
        public static string SasTokenDescriptionLong { get { return LC.L(@"The Azure shared access signature (SAS) token which can be obtained by selecting the ""Shared access signature"" blade on the storage account dashboard, or inside a container blade"); } }
        public static string SasTokenDescriptionShort { get { return LC.L(@"The SAS token"); } }
        public static string NoAccessKeyOrSasToken { get { return LC.L(@"No Azure access key or SAS token given"); } }
        public static string AuthPasswordDescriptionLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string AuthPasswordDescriptionShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string AuthUsernameDescriptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string AuthUsernameDescriptionShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string Description_v2 { get { return LC.L(@"This backend can read and write data to Azure blob storage.  Allowed formats are: ""azure://bucketname"""); } }
        public static string ErrorDeleteFile { get { return LC.L(@"Error on deleting file: {0}"); } }
        public static string ErrorReadFile { get { return LC.L(@"Error reading file: {0}"); } }
        public static string ErrorWriteFile { get { return LC.L(@"Error writing file: {0}"); } }
        public static string MissingContainerError(string containerName, string message) { return LC.L(@"The container {0} was not found. Message: {1}", containerName, message); }
    }
}
