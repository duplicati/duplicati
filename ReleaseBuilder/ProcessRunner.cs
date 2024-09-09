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
    private const string OSSLOrganization = "Duplicati";
    /// <summary>
    /// The url to encode in the Authenticode certificate
    /// </summary>
    private const string OSSLUrl = "https://duplicati.com";

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

            var args = new[] {
                osslsigncode, "sign",
                "-pkcs12", pfxfile,
                "-pass", pfxpassword,
                "-n", OSSLOrganization,
                "-i", OSSLUrl,
                "-h", hashalg,
                first ? "" : "-nest",
                "-t", $"http://timestamp.digicert.com?alg={hashalg}",
                "-in", executable,
                "-out", tmp
            };

            await ProcessHelper.Execute(args.Where(x => !string.IsNullOrWhiteSpace(x)));
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
    /// <returns>An awaitable task</returns>
    public static Task MacOSCodeSign(string codesign, string codesignIdentity, string entitlementFile, string file)
        => ProcessHelper.Execute([
            codesign,
            "--force",
            "--timestamp",
            "--options=runtime",
            "--entitlements", entitlementFile,
            "--sign", codesignIdentity,
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
