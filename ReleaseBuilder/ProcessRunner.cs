namespace ReleaseBuilder;

public static class ProcessRunner
{
    /// <summary>
    /// The hash algorithms used for signing with Authenticode
    /// </summary>
    private static readonly IReadOnlyList<string> OSSLHashAlgs = new[] { "sha1", "sha256" };

    /// <summary>
    /// The company name to encode in the Authenticode certificate
    /// </summary>
    private const string OSSLOrganization = "Duplicati Inc";
    /// <summary>
    /// The url to encode in the Authenticode certificate
    /// </summary>
    private const string OSSLUrl = "https://duplicati.com";

    /// <summary>
    /// Performs code signing of the <paramref name="executable"/>
    /// </summary>
    /// <param name="jsign">The path to the jsign binary</param>
    /// <param name="keypin">The password to decrypt the PFX file</param>
    /// <param name="executable">The executable to sign, in-place</param>
    /// <returns>An awaitable task</returns>
    public static async Task JsignCodeSign(string jsign, string keypin, string executable)
    {
        var first = true;
        foreach (var hashalg in OSSLHashAlgs)
        {
            await ProcessHelper.Execute([
                jsign, "sign",
                "--storetype", "PIV",
                "--storepass", keypin,
                "--alias", "AUTHENTICATION",
                "--name", OSSLOrganization,
                "--url", OSSLUrl,
                "--alg", hashalg,
                first ? "--replace" : null,
                "--tsaurl", "http://ts.ssl.com",
                "--tsmode", "RFC3161",
                executable
            ]);


            first = false;
        }
    }

    /// <summary>
    /// Performs code signing of the <paramref name="executable"/>
    /// </summary>
    /// <param name="osslsigncode">The path to the signcode binary</param>
    /// <param name="pfxfile">The path to the PFX file</param>
    /// <param name="pfxpassword">The password to decrypt the PFX file</param>
    /// <param name="executable">The executable to sign, in-place</param>
    /// <returns>An awaitable task</returns>
    public static async Task OsslCodeSign(string osslsigncode, string pfxfile, string pfxpassword, string executable)
    {
        var first = true;
        foreach (var hashalg in OSSLHashAlgs)
        {
            var tmp = Path.GetTempFileName();
            File.Delete(tmp);

            // It is also possible to use a PKCS#11 module for the key, like this:
            // osslsigncode sign -h sha256 
            //   -pkcs11module /opt/homebrew/lib/libykcs11.dylib 
            //   -certs public-key.pem
            //   -key 'pkcs11:pin-value={pin}' 
            //   -ts http://ts.ssl.com 
            //   -in {executable} 
            //   -out {tmp}


            await ProcessHelper.Execute([
                osslsigncode, "sign",
                "-pkcs12", pfxfile,
                "-pass", pfxpassword,
                "-n", OSSLOrganization,
                "-i", OSSLUrl,
                "-h", hashalg,
                first ? null : "-nest",
                "-t", $"http://timestamp.digicert.com?alg={hashalg}",
                "-in", executable,
                "-out", tmp
            ]);

            File.Move(tmp, executable, true);

            first = false;
        }
    }

    /// <summary>
    /// Runs MacOS codesign on a single file
    /// </summary>
    /// <param name="codesign">The path to the codesign binary</param>
    /// <param name="codesignIdentity">The identity used for codesign</param>
    /// <param name="entitlementFile">The entitlements to activate for the file</param>
    /// <param name="file">The file to sign</param>
    /// <param name="deep">Whether to sign deeply</param>
    /// <returns>An awaitable task</returns>
    public static Task MacOSCodeSign(string codesign, string codesignIdentity, string entitlementFile, string file, bool deep)
        => ProcessHelper.Execute([
            codesign,
            (deep ? "--deep" : null),
            "--force",
            "--timestamp",
            "--options=runtime",
            "--entitlements", entitlementFile,
            "--sign", codesignIdentity,
            file
        ]);

    /// <summary>
    /// Verifies the MacOS codesign of a file or app bundle
    /// </summary>
    /// <param name="codesign">The path to the codesign binary</param>
    /// <param name="file">The file to verify</param>
    /// <returns>An awaitable task</returns>
    public static Task MacOSVerifyCodeSign(string codesign, string file)
        => ProcessHelper.Execute([
            codesign,
            "--verify",
            "--deep",
            "--strict",
            file
        ]);

    /// <summary>
    /// Runs MacOS codesign on a single file
    /// </summary>
    /// <param name="productsign">The path to the productsign binary</param>
    /// <param name="codesignIdentity">The identity used for codesign</param>
    /// <param name="entitlementFile">The entitlements to activate for the file</param>
    /// <param name="file">The file to sign</param>
    /// <returns>An awaitable task</returns>
    public static async Task MacOSProductSign(string productsign, string codesignIdentity, string file)
    {
        var outputfile = file + ".signed";

        await ProcessHelper.Execute([
            productsign,
            "--sign", codesignIdentity,
            file,
            outputfile
        ]);

        File.Move(outputfile, file, true);
    }
}
