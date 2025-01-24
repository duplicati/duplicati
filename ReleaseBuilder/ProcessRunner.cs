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
namespace ReleaseBuilder;

public static class ProcessRunner
{
    /// <summary>
    /// The hash algorithms used for signing with Authenticode
    /// </summary>
    private static readonly IReadOnlyList<string> OSSLHashAlgs = ["sha256"];

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
