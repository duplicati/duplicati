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

namespace Duplicati.Library.SecretProvider.Strings;

internal static class EnvironmentSecretProvider
{
    public static string DisplayName => LC.L("Secrets from environment variables");
    public static string Description => LC.L(
@"Secret provider that reads secrets from environment variables.
Example use:
    env:

For environment variables, the lookup is case-insensitive.
");
}

internal static class FileSecretProvider
{
    public static string DisplayName => LC.L("Secrets from a file");
    public static string Description => LC.L(
@"Secret provider that reads secrets from a file
Example use:
    file://path/to/file?passphrase=secret

The file should be a JSON-encoded object with the secrets as key-value pairs.
If the file is not encrypted with a passphrase, the passphrase parameter can be omitted.
The file must be encrypted with AESCrypt if encryption is desired.
For file-based secrets, the lookup is case-insensitive.
");

    public static string PassphraseDescriptionShort => LC.L(@"The decryption passphrase");
    public static string PassphraseDescriptionLong => LC.L(@"The passphrase to use for decrypting the file with secrets");
}

internal static class AWSSecretProvider
{
    public static string DisplayName => LC.L("Secrets from AWS Secrets Manager");
    public static string Description => LC.L(
@"Secret provider that reads secrets from AWS Secrets Manager
Example use:
    aws://?access-key=...&secret-key=...&region-endpoint=us-east-1&secrets=secret1,secret2

Either the region endpoint or the service URL must be provided.
Each secret is retrieved in the order specified until all requested keys are found.
Each secret must be a key-value pair type secret.
");
    public static string AccessKeyDescriptionShort => LC.L(@"The AWS access key");
    public static string AccessKeyDescriptionLong(string envname) => LC.L(@$"The access key to use for authentication with AWS. Can also be supplied via the environment variable {envname}");
    public static string SecretKeyDescriptionShort => LC.L(@"The AWS secret key");
    public static string SecretKeyDescriptionLong(string envname) => LC.L(@$"The secret key to use for authentication with AWS. Can also be supplied via the environment variable {envname}");
    public static string RegionEndpointDescriptionShort => LC.L(@"The AWS region endpoint");
    public static string RegionEndpointDescriptionLong(string envname) => LC.L(@$"The region endpoint to use for communication with AWS. Can also be supplied via the environment variable {envname}");
    public static string ServiceURLDescriptionShort => LC.L(@"The AWS service URL");
    public static string ServiceURLDescriptionLong(string envname) => LC.L(@$"The service URL to use for communication with AWS. Can also be supplied via the environment variable {envname}");
    public static string SecretsDescriptionShort => LC.L(@"The secret names to retrieve");
    public static string SecretsDescriptionLong => LC.L(@"The names of the secrets to retrieve from AWS Secrets Manager, separated by semicolons or commas. Secrets are retrieved in the order specified until all requested keys are found.");
    public static string CaseSensitiveDescriptionShort => LC.L(@"Case sensitivity");
    public static string CaseSensitiveDescriptionLong => LC.L(@"Whether to use case-sensitive matching for secret names");
}

internal static class HCVaultSecretProvider
{
    public static string DisplayName => LC.L("Secrets from HashiCorp Vault");
    public static string Description => LC.L(
@"Secret provider that reads secrets from HashiCorp Vault
Example use:
    hcv://localhost:1234?token=...&mount=secret&secrets=secret1,secret2

The secrets parameter should be a comma- or semicolon-separated list of secret names to retrieve from the Vault.
Each vault secret is retrieved in the order specified until all requested keys are found.
");
    public static string TokenDescriptionShort => LC.L(@"The Vault token");
    public static string TokenDescriptionLong => LC.L(@"The token to use for authentication with HashiCorp Vault");
    public static string ProtocolDescriptionShort => LC.L(@"The vault server protocol");
    public static string ProtocolDescriptionLong => LC.L(@"The protocol to use for communication with the Vault server");
    public static string SecretsDescriptionShort => LC.L(@"Secret entries");
    public static string SecretsDescriptionLong => LC.L(@"The names of the secrets (aka Vault Apps) to retrieve from the Vault, separated by semicolons or commas. Secrets are retrieved in the order specified until all requested keys are found.");
    public static string ClientIdDescriptionShort => LC.L(@"The client ID");
    public static string ClientIdDescriptionLong(string envname) => LC.L(@$"The client ID to use for authentication with HashiCorp Vault. Can also be supplied via the environment variable {envname}");
    public static string ClientSecretDescriptionShort => LC.L(@"The client secret");
    public static string ClientSecretDescriptionLong(string envname) => LC.L(@$"The client secret to use for authentication with HashiCorp Vault. Can also be supplied via the environment variable {envname}");
    public static string MountPointDescriptionShort => LC.L(@"The mount point");
    public static string MountPointDescriptionLong => LC.L(@"The mount point for the secrets in the Vault");
    public static string CaseSensitiveDescriptionShort => LC.L(@"Case sensitivity");
    public static string CaseSensitiveDescriptionLong => LC.L(@"Whether to use case-sensitive matching for secret names");
}

internal static class GCSSecretProvider
{
    public static string DisplayName => LC.L("Secrets from Google Cloud Storage Secret Manager");
    public static string Description => LC.L(
@"Secret provider that reads secrets from Google Cloud Storage Secret Manager
Example use:
    gcs://?project-id=...&version=latest

If the access token is not supplied, the default GCS credentials from the machine will be used.
Use the accesstoken property to specify a custom access token.

");
    public static string ApiTypeDescriptionShort => LC.L(@"The API type to use");
    public static string ApiTypeDescriptionLong => LC.L(@"The type of API to use for communication with Google Cloud Storage");
    public static string ProjectIdDescriptionShort => LC.L(@"The GCP project ID");
    public static string ProjectIdDescriptionLong => LC.L(@"The ID of the Google Cloud Platform project to use for authentication");
    public static string AccessTokenDescriptionShort => LC.L(@"The access token");
    public static string AccessTokenDescriptionLong => LC.L(@"The access token to use for authentication with Google Cloud Storage. If not supplied, the default GCS credentials from the machine will be used.");
    public static string VersionDescriptionShort => LC.L(@"The secret version to get (or alias)");
    public static string VersionDescriptionLong => LC.L(@"The version of the secret to retrieve from Google Cloud Storage. If not supplied, the latest version will be used.");
}

internal static class AzureSecretProvider
{
    public static string DisplayName => LC.L("Secrets from Azure Key Vault");
    public static string Description => LC.L(
@"Secret provider that reads secrets from Azure Key Vault
Example use:
    az://?keyvault-name=...&auth-type=ManagedIdentity

If a client ID and secret are provided, use the auth type ClientSecret:
    az://?keyvault-name=...&auth-type=ClientSecret&tenant-id=...&client-id=...&client-secret=...

If a username and password are provided, use the auth type UsernamePassword:
    az://?keyvault-name=...&auth-type=UsernamePassword&tenant-id=...&clientid=...&username=...&password=...

If the vault-uri parameter is not provided, the keyvault-name parameter will be used to construct the URI.
");
    public static string KeyVaultNameDescriptionShort => LC.L(@"The name of the Azure Key Vault");
    public static string KeyVaultNameDescriptionLong => LC.L(@"The name of the Azure Key Vault to use for retrieving secrets");
    public static string ConnectionTypeDescriptionShort => LC.L(@"The connection type to use");
    public static string ConnectionTypeDescriptionLong => LC.L(@"The type of connection to use for communication with Azure Key Vault");
    public static string VaultUriDescriptionShort => LC.L(@"The URI of the Azure Key Vault");
    public static string VaultUriDescriptionLong => LC.L(@"The URI of the Azure Key Vault to use for retrieving secrets");
    public static string TenantIdDescriptionShort => LC.L(@"The Azure tenant ID");
    public static string TenantIdDescriptionLong => LC.L(@"The ID of the Azure tenant to use for authentication");
    public static string ClientIdDescriptionShort => LC.L(@"The Azure client ID");
    public static string ClientIdDescriptionLong => LC.L(@"The ID of the Azure client to use for authentication");
    public static string ClientSecretDescriptionShort => LC.L(@"The Azure client secret");
    public static string ClientSecretDescriptionLong => LC.L(@"The secret to use for authentication with Azure");
    public static string UsernameDescriptionShort => LC.L(@"The Azure username");
    public static string UsernameDescriptionLong => LC.L(@"The username to use for authentication with Azure");
    public static string PasswordDescriptionShort => LC.L(@"The Azure password");
    public static string PasswordDescriptionLong => LC.L(@"The password to use for authentication with Azure");
    public static string AuthenticationTypeDescriptionShort => LC.L(@"The authentication type to use");
    public static string AuthenticationTypeDescriptionLong => LC.L(@"The type of authentication to use for communication with Azure Key Vault");
}

internal static class MacOSKeyChainProvider
{
    public static string DisplayName => LC.L("Secrets from macOS Keychain");
    public static string Description => LC.L("Secret provider that reads secrets from the macOS Keychain");
    public static string ServiceDescriptionShort => LC.L("The service name");
    public static string ServiceDescriptionLong => LC.L("The service name to use for retrieving secrets from the macOS Keychain");
    public static string AccountDescriptionShort => LC.L("The account name");
    public static string AccountDescriptionLong => LC.L("The account name to use for retrieving secrets from the macOS Keychain");
    public static string TypeDescriptionShort => LC.L("The type of password to get");
    public static string TypeDescriptionLong => LC.L("The type of password to get from the macOS Keychain");
}

internal static class UnixPassProvider
{
    public static string DisplayName => LC.L("Secrets from Unix pass");
    public static string Description => LC.L("Secret provider that reads secrets from the Unix pass password manager");
    public static string PassCommandDescriptionShort => LC.L("The pass command");
    public static string PassCommandDescriptionLong => LC.L("The command to use for retrieving secrets from the Unix pass password manager");
}

internal static class WindowsCredentialManagerProvider
{
    public static string DisplayName => LC.L("Secrets from Windows Credential Manager");
    public static string Description => LC.L("Secret provider that reads secrets from the Windows Credential Manager");
}

internal static class LibSecretLinuxProvider
{
    public static string DisplayName => LC.L("Secrets from libsecret");
    public static string Description => LC.L("Secret provider that reads secrets from the libsecret password manager");
    public static string CollectionDescriptionShort => LC.L("The collection name");
    public static string CollectionDescriptionLong => LC.L("The collection name to use for retrieving secrets from the libsecret password manager");
    public static string CaseSensitiveDescriptionShort => LC.L("Case sensitivity");
    public static string CaseSensitiveDescriptionLong => LC.L("Whether to use case-sensitive matching for secret names");
}

