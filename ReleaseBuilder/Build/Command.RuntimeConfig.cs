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
using System.Security.Cryptography;

namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Setup of the current runtime information
    /// </summary>
    /// <remarks>
    /// Constructs a new <see cref="RuntimeConfig"/>
    /// </remarks>
    /// <param name="releaseInfo">The release info to use</param>
    /// <param name="keyfilePassword">The keyfile password to use</param>
    /// <param name="signKeys">The sign keys</param>
    /// <param name="changelogNews">The changelog news</param>
    /// <param name="input">The command input</param>
    private class RuntimeConfig(
        Configuration configuration,
        ReleaseInfo releaseInfo,
        IEnumerable<RSA> signKeys,
        string keyfilePassword,
        string changelogNews,
        CommandInput input
    )
    {

        /// <summary>
        /// The cached password for the pfx file
        /// </summary>
        private string? _pfxPassword = null;

        /// <summary>
        /// The commandline input
        /// </summary>
        private CommandInput Input => input;
        /// <summary>
        /// The configuration to use
        /// </summary>
        public Configuration Configuration => configuration;
        /// <summary>
        /// The release info for this run
        /// </summary>
        public ReleaseInfo ReleaseInfo => releaseInfo;

        /// <summary>
        /// The keyfile password for this run
        /// </summary>
        public IEnumerable<RSA> SignKeys => signKeys;

        /// <summary>
        /// The primary password
        /// </summary>
        public string KeyfilePassword => keyfilePassword;

        /// <summary>
        /// The changelog news
        /// </summary>
        public string ChangelogNews => changelogNews;

        /// <summary>
        /// Gets the PFX password and throws if not possible
        /// </summary>
        public string PfxPassword
            => string.IsNullOrWhiteSpace(_pfxPassword)
                ? _pfxPassword = GetAuthenticodePassword(KeyfilePassword)
                : _pfxPassword;

        /// <summary>
        /// Cache value for checking if authenticode signing is enabled
        /// </summary>
        private bool? _useAuthenticodeSigning;

        /// <summary>
        /// Cache value for checking if jsign tool should be used for authenticode signing
        /// </summary>
        private bool? _useJsignToolForAuthenticode;

        /// <summary>
        /// Checks if Authenticode signing should be enabled
        /// </summary>
        public void ToggleAuthenticodeSigning()
        {
            if (!_useAuthenticodeSigning.HasValue)
            {
                if (Input.DisableAuthenticode)
                {
                    _useAuthenticodeSigning = false;
                    return;
                }

                if (Configuration.IsAuthenticodePossibleWithSignTool())
                {
                    _useAuthenticodeSigning = true;
                    _useJsignToolForAuthenticode = false;
                    return;
                }

                if (Configuration.IsAuthenticodePossibleWithJsignTool())
                {
                    if (string.IsNullOrWhiteSpace(Input.SignkeyPin))
                    {
                        var signKeyPin = EnvHelper.GetEnvKey("SIGNKEY_PIN", "");
                        if (string.IsNullOrWhiteSpace(signKeyPin))
                            signKeyPin = ConsoleHelper.ReadPassword("Enter the pin for the signing key");
                        input = input with { SignkeyPin = signKeyPin };
                    }

                    if (!string.IsNullOrWhiteSpace(Input.SignkeyPin))
                    {
                        _useAuthenticodeSigning = true;
                        _useJsignToolForAuthenticode = true;
                        return;
                    }
                }

                if (ConsoleHelper.ReadInput("Configuration missing for jsign/signtool/osslsigncode, continue without signing executables?", "Y", "n") == "Y")
                {
                    _useAuthenticodeSigning = false;
                    _useJsignToolForAuthenticode = false;
                    return;
                }

                throw new Exception("Configuration is not set up for jsign/signtool/osslsigncode");
            }
        }

        /// <summary>
        /// Cache value for checking if codesign is possible
        /// </summary>
        private bool? _useCodeSignSigning;

        /// <summary>
        /// Checks if codesign is enabled
        /// </summary>
        public void ToggleSignCodeSigning()
        {
            if (!_useCodeSignSigning.HasValue)
            {
                if (Input.DisableSignCode)
                {
                    _useCodeSignSigning = false;
                    return;
                }

                if (!OperatingSystem.IsMacOS())
                    _useCodeSignSigning = false;
                else if (Configuration.IsCodeSignPossible())
                    _useCodeSignSigning = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration missing for signcode, continue without signing executables?", "Y", "n") == "Y")
                    {
                        _useCodeSignSigning = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for signcode");
                }
            }
        }

        /// <summary>
        /// Cache value for checking if docker build is enabled
        /// </summary>
        private bool? _dockerBuild;

        /// <summary>
        /// Checks if docker build is enabled
        /// </summary>
        public async Task ToggleDockerBuild()
        {
            if (!_dockerBuild.HasValue)
            {
                try
                {
                    var res = await ProcessHelper.ExecuteWithOutput([Configuration.Commands.Docker!, "ps"], suppressStdErr: true);
                    _dockerBuild = true;
                }
                catch
                {

                    if (ConsoleHelper.ReadInput("Docker does not seem to be running, continue without docker builds?", "Y", "n") == "Y")
                    {
                        _dockerBuild = false;
                        return;
                    }

                    throw new Exception("Docker is not running, and is required for building Docker images");
                }
            }
        }

        /// <summary>
        /// Cache value for checking if notarize is enabled
        /// </summary>
        private bool? _useNotarizeSigning;

        /// <summary>
        /// Checks if notarize signing is enabled
        /// </summary>
        public void ToggleNotarizeSigning()
        {
            if (!_useNotarizeSigning.HasValue)
            {
                if (Input.DisableNotarizeSigning)
                {
                    _useNotarizeSigning = false;
                    return;
                }

                if (!OperatingSystem.IsMacOS())
                    _useNotarizeSigning = false;
                else if (Configuration.IsNotarizePossible())
                    _useNotarizeSigning = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration missing for notarize, continue without notarizing executables?", "Y", "n") == "Y")
                    {
                        _useNotarizeSigning = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for notarize");
                }
            }
        }

        /// <summary>
        /// Cache value for checking if GPG signing is enabled
        /// </summary>
        private bool? _useGpgSigning;

        /// <summary>
        /// Checks if GPG signing is enabled
        /// </summary>
        public void ToggleGpgSigning()
        {
            if (!_useGpgSigning.HasValue)
            {
                if (Input.DisableGpgSigning)
                {
                    _useGpgSigning = false;
                    return;
                }

                if (Configuration.IsGpgPossible())
                    _useGpgSigning = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration missing for gpg, continue without gpg signing packages?", "Y", "n") == "Y")
                    {
                        _useGpgSigning = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for gpg");
                }
            }
        }

        /// <summary>
        /// Cache value for checking if S3 upload is enabled
        /// </summary>
        private bool? _useS3Upload;

        /// <summary>
        /// Checks if S3 upload is enabled
        /// </summary>
        public void ToggleS3Upload()
        {
            if (!_useS3Upload.HasValue)
            {
                if (Input.DisableS3Upload)
                {
                    _useS3Upload = false;
                    return;
                }

                if (Configuration.IsAwsUploadPossible())
                    _useS3Upload = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration missing for awscli, continue without uploading to S3?", "Y", "n") == "Y")
                    {
                        _useS3Upload = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for awscli");
                }
            }
        }

        /// <summary>
        /// Cache value for checking if Github upload is enabled
        /// </summary>
        private bool? _useGithubUpload;

        /// <summary>
        /// Checks if Github upload is enabled
        /// </summary>
        /// <param name="channel">The release channel to use</param>
        public void ToggleGithubUpload(ReleaseChannel channel)
        {
            if (!_useGithubUpload.HasValue)
            {
                if (Input.DisableGithubUpload || channel == ReleaseChannel.Debug || channel == ReleaseChannel.Nightly)
                {
                    _useGithubUpload = false;
                    return;
                }

                if (Configuration.IsGithubUploadPossible())
                    _useGithubUpload = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration is missing a Github token, continue without uploading to Github?", "Y", "n") == "Y")
                    {
                        _useGithubUpload = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for github releases");
                }
            }
        }

        /// <summary>
        /// Cache value for checking if update server reload is enabled
        /// </summary>
        private bool? _useUpdateServerReload;

        /// <summary>
        /// Checks if update server reload is enabled
        /// </summary>
        public void ToggleUpdateServerReload()
        {
            if (!_useUpdateServerReload.HasValue)
            {
                if (Input.DisableUpdateServerReload)
                {
                    _useUpdateServerReload = false;
                    return;
                }

                if (Configuration.IsUpdateServerReloadPossible())
                    _useUpdateServerReload = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration missing for update server, continue without reloading the update server?", "Y", "n") == "Y")
                    {
                        _useUpdateServerReload = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for update server");
                }
            }
        }

        /// <summary>
        /// Cache value for checking if forum posting is enabled
        /// </summary>
        private bool? _useDiscourseAnnounce;

        /// <summary>
        /// Checks if forum posting is enabled
        /// </summary>
        /// <param name="channel">The release channel to use</param>
        public void ToogleDiscourseAnnounce(ReleaseChannel channel)
        {
            if (!_useDiscourseAnnounce.HasValue)
            {
                if (Input.DisableDiscordAnnounce || channel == ReleaseChannel.Debug || channel == ReleaseChannel.Nightly)
                {
                    _useDiscourseAnnounce = false;
                    return;
                }

                if (Configuration.IsDiscourseAnnouncePossible())
                    _useDiscourseAnnounce = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration missing for forum posting, continue without posting to the forum?", "Y", "n") == "Y")
                    {
                        _useDiscourseAnnounce = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for forum posting");
                }
            }
        }

        /// <summary>
        /// Returns a value indicating if codesign is enabled
        /// </summary>
        public bool UseCodeSignSigning => _useCodeSignSigning!.Value;

        /// <summary>
        /// Returns a value indicating if authenticode signing is enabled
        /// </summary>
        public bool UseAuthenticodeSigning => _useAuthenticodeSigning!.Value;

        /// <summary>
        /// Returns a value indicating if authenticode signing is enabled
        /// </summary>
        public bool UseJsingToolForAuthenticode => _useJsignToolForAuthenticode!.Value;

        /// <summary>
        /// Returns a value indicating if notarize is enabled
        /// </summary>
        public bool UseNotarizeSigning => _useNotarizeSigning!.Value;

        /// <summary>
        /// Returns a value indicating if GPG signing is enabled
        /// </summary>
        public bool UseGPGSigning => _useGpgSigning!.Value;

        /// <summary>
        /// Returns a value indicating if docker build is enabled
        /// </summary>
        public bool UseDockerBuild => _dockerBuild!.Value;

        /// <summary>
        /// Returns a value indicating if S3 upload is enabled
        /// </summary>
        public bool UseS3Upload => _useS3Upload!.Value;

        /// <summary>
        /// Returns a value indicating if Github upload is enabled
        /// </summary>
        public bool UseGithubUpload => _useGithubUpload!.Value;

        /// <summary>
        /// Returns a value indicating if update server reload is enabled
        /// </summary>
        public bool UseUpdateServerReload => _useUpdateServerReload!.Value;

        /// <summary>
        /// Returns a value indicating if forum posting is enabled
        /// </summary>
        public bool UseForumPosting => _useDiscourseAnnounce!.Value;

        /// <summary>
        /// Gets the MacOS app bundle name
        /// </summary>
        public string MacOSAppName => Input.MacOSAppName;

        /// <summary>
        /// The docker repository to use
        /// </summary>
        public string DockerRepo => Input.DockerRepo;

        /// <summary>
        /// Gets a value indicating if pushing should be enabled
        /// </summary>
        public bool PushToDocker => !Input.DisableDockerPush;

        /// <summary>
        /// Decrypts the password file and returns the PFX password
        /// </summary>
        /// <param name="keyfilepassword">Password for the password file</param>
        /// <returns>The Authenticode password</returns>
        private string GetAuthenticodePassword(string keyfilepassword)
            => EncryptionHelper.DecryptPasswordFile(Configuration.ConfigFiles.AuthenticodePasswordFile, keyfilepassword).Trim();

        /// <summary>
        /// Performs authenticode signing if enabled
        /// </summary>
        /// <param name="file">The file to sign</param>
        /// <returns>An awaitable task</returns>
        public Task AuthenticodeSign(string file)
        {
            if (!UseAuthenticodeSigning)
                return Task.CompletedTask;

            if (UseJsingToolForAuthenticode)
                return ProcessRunner.JsignCodeSign(Configuration.Commands.JSign!, Input.SignkeyPin, file);
            else
                return ProcessRunner.OsslCodeSign(
                    Configuration.Commands.SignCode!,
                    Configuration.ConfigFiles.AuthenticodePfxFile,
                    PfxPassword,
                    file
                );
        }

        /// <summary>
        /// Performs codesign on the given file
        /// </summary>
        /// <param name="file">The file to sign</param>
        /// <param name="deep">A value indicating if deep signing should be used</param>
        /// <param name="entitlements">The entitlements to apply</param>
        /// <returns>An awaitable task</returns>
        public Task Codesign(string file, bool deep, string entitlements)
            => UseCodeSignSigning
                ? ProcessRunner.MacOSCodeSign(
                    Configuration.Commands.Codesign!,
                    Configuration.ConfigFiles.CodesignIdentity,
                    entitlements,
                    file,
                    deep
                )
                : Task.CompletedTask;

        /// <summary>
        /// Verifies the codesign of the given file or app bundle
        /// </summary>
        /// <param name="file">The file to verify</param>
        /// <returns>An awaitable task</returns>
        public Task VerifyCodeSign(string file)
            => UseCodeSignSigning
                ? ProcessRunner.MacOSVerifyCodeSign(
                    Configuration.Commands.Codesign!,
                    file
                )
                : Task.CompletedTask;

        /// <summary>
        /// Performs productsign on the given file
        /// </summary>
        /// <param name="file">The file to sign</param>
        /// <returns>An awaitable task</returns>
        public Task Productsign(string file)
            => UseCodeSignSigning
                ? ProcessRunner.MacOSProductSign(
                    Configuration.Commands.Productsign!,
                    Configuration.ConfigFiles.CodesignIdentity,
                    file
                )
                : Task.CompletedTask;

    }

}
