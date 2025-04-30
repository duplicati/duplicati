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

namespace Duplicati.Backend.Tests.Base;

/// <summary>
/// Base class for tests that require a running SFTPGo instance which will
/// be used to test the FTP, Webdav & SSH backends.
/// 
/// It handles the creation of the necessary configuration files and certificates
/// </summary>
public class BaseSftpgoTest : BaseTest
{
    protected static readonly string TEST_USER_NAME = "testuser";
    protected static readonly string TEST_USER_PASSWORD = "testpassword";
    protected static readonly string CERTIFICATE_FILE = "fullchain.pem";
    protected static readonly string CERTIFICATE_PRIVATE_KEY_FILE = "privkey.pem";
    
    /// <summary>
    /// Creates a josn file with the configuration for the SFTPGo instance
    /// </summary>
    /// <param name="directoryInfo">directory in which to create the file</param>
    protected void CreateUsersFile(DirectoryInfo directoryInfo)
    {
        var sftpgoConfig = "{\"users\":[{\"id\":1,\"status\":1,\"username\":\"testuser\",\"password\":\"$2a$10$BTqu.odQz6jLQAi70KbQJOYBcRHpbhP4uxqcfFXZalq4zUsQZGj/6\",\"has_password\":true,\"home_dir\":\"/srv/sftpgo/data/testuser\",\"uid\":0,\"gid\":0,\"max_sessions\":0,\"quota_size\":0,\"quota_files\":0,\"permissions\":{\"/\":[\"*\"]},\"upload_data_transfer\":0,\"download_data_transfer\":0,\"total_data_transfer\":0,\"created_at\":1729521839754,\"updated_at\":1729521839754,\"last_password_change\":1729521839754,\"filters\":{\"hooks\":{\"external_auth_disabled\":false,\"pre_login_disabled\":false,\"check_password_disabled\":false},\"totp_config\":{\"secret\":{}}},\"filesystem\":{\"provider\":0,\"osconfig\":{},\"s3config\":{\"access_secret\":{}},\"gcsconfig\":{\"credentials\":{}},\"azblobconfig\":{\"account_key\":{},\"sas_url\":{}},\"cryptconfig\":{\"passphrase\":{}},\"sftpconfig\":{\"password\":{},\"private_key\":{},\"key_passphrase\":{}},\"httpconfig\":{\"password\":{},\"api_key\":{}}}}],\"groups\":[],\"folders\":[],\"admins\":[{\"id\":1,\"status\":1,\"username\":\"admin\",\"password\":\"$2a$10$HJGY10EuEPPajgut3OMTte5NXqbn1RXMTzy8BUA639a6qzinOjIx6\",\"permissions\":[\"*\"],\"filters\":{\"require_two_factor\":false,\"totp_config\":{\"secret\":{}},\"preferences\":{}},\"created_at\":1729521817148,\"updated_at\":1729521817148,\"last_login\":1729521817152}],\"api_keys\":[],\"shares\":[],\"event_actions\":[],\"event_rules\":[],\"roles\":[],\"ip_lists\":[],\"configs\":{},\"version\":16}";

        using var usersFile = File.Create(Path.Combine(directoryInfo.FullName, "users.json"));

        using var userFileWritter = new StreamWriter(usersFile);

        userFileWritter.WriteLine(sftpgoConfig);

    }

    /// <summary>
    /// Create a self signed certificate to be used on WebDav and SFTP connections.
    /// 
    /// Its created on a temporary directory and the private key and public key are saved in PEM format.
    /// 
    /// It is up to the caller to delete the files albeit its in the OS temporary directory.
    /// </summary>
    /// <returns>Returns the directory in which certificate was created</returns>
    protected DirectoryInfo CreateHttpsCertificates()
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN=TestCertificateForWebdav",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DataEncipherment | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature,
                false));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")], false));

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1),
            DateTimeOffset.Now.AddDays(30));

        if (OperatingSystem.IsWindows()) certificate.FriendlyName = "Test Certificate";

        var directoryInfo = Directory.CreateTempSubdirectory("TemporaryCertificates");

        using var privateKeyFile = File.Create(Path.Combine(directoryInfo.FullName, CERTIFICATE_PRIVATE_KEY_FILE));
        using var publicKeyFile = File.Create(Path.Combine(directoryInfo.FullName, CERTIFICATE_FILE));

        using var privateKeyWriter = new StreamWriter(privateKeyFile);
        using var publicKeyWriter = new StreamWriter(publicKeyFile);
        privateKeyWriter.WriteLine(certificate.GetRSAPrivateKey()!.ExportRSAPrivateKeyPem());
        publicKeyWriter.WriteLine(certificate.ExportCertificatePem());

        return directoryInfo;
    }

}