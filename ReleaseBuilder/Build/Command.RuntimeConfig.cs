using System.Security.Cryptography;

namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Setup of the current runtime information
    /// </summary>
    private class RuntimeConfig
    {
        /// <summary>
        /// Constructs a new <see cref="RuntimeConfig"/>
        /// </summary>
        /// <param name="releaseInfo">The release info to use</param>
        /// <param name="keyfilePassword">The keyfile password to use</param>
        /// <param name="signKeys">The sign keys</param>
        /// <param name="changelogNews">The changelog news</param>
        /// <param name="input">The command input</param>
        public RuntimeConfig(ReleaseInfo releaseInfo, IEnumerable<RSA> signKeys, string keyfilePassword, string changelogNews, CommandInput input)
        {
            ReleaseInfo = releaseInfo;
            SignKeys = signKeys;
            KeyfilePassword = keyfilePassword;
            ChangelogNews = changelogNews;
            Input = input;
        }

        /// <summary>
        /// The cached password for the pfx file
        /// </summary>
        private string? _pfxPassword = null;

        /// <summary>
        /// The commandline input
        /// </summary>
        private CommandInput Input { get; }

        /// <summary>
        /// The release info for this run
        /// </summary>
        public ReleaseInfo ReleaseInfo { get; }

        /// <summary>
        /// The keyfile password for this run
        /// </summary>
        public IEnumerable<RSA> SignKeys { get; }

        /// <summary>
        /// The primary password
        /// </summary>
        public string KeyfilePassword { get; }

        /// <summary>
        /// The changelog news
        /// </summary>
        public string ChangelogNews { get; }

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

                if (Program.Configuration.IsAuthenticodePossible())
                    _useAuthenticodeSigning = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration missing for osslsigncode, continue without signing executables?", "Y", "n") == "Y")
                    {
                        _useAuthenticodeSigning = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for osslsigncode");
                }
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
                else if (Program.Configuration.IsCodeSignPossible())
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
                    var res = await ProcessHelper.ExecuteWithOutput([Program.Configuration.Commands.Docker!, "ps"], suppressStdErr: true);
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
                else if (Program.Configuration.IsNotarizePossible())
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

        public void ToggleGpgSigning()
        {
            if (!_useGpgSigning.HasValue)
            {
                if (Input.DisableGpgSigning)
                {
                    _useGpgSigning = false;
                    return;
                }

                if (ConsoleHelper.ReadInput("Configuration missing for gpg, continue without gpg signing packages?", "Y", "n") == "Y")
                {
                    _useGpgSigning = false;
                    return;
                }

                throw new Exception("Configuration is not set up for gpg");
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
            => EncryptionHelper.DecryptPasswordFile(Program.Configuration.ConfigFiles.AuthenticodePasswordFile, keyfilepassword).Trim();

        /// <summary>
        /// Performs authenticode signing if enabled
        /// </summary>
        /// <param name="file">The file to sign</param>
        /// <returns>An awaitable task</returns>
        public Task AuthenticodeSign(string file)
            => UseAuthenticodeSigning
                ? ProcessRunner.OsslCodeSign(
                    Program.Configuration.Commands.OsslSignCode!,
                    Program.Configuration.ConfigFiles.AuthenticodePfxFile,
                    PfxPassword,
                    file)
                : Task.CompletedTask;

        /// <summary>
        /// Performs codesign on the given file
        /// </summary>
        /// <param name="file">The file to sign</param>
        /// <param name="entitlements">The entitlements to apply</param>
        /// <returns>An awaitable task</returns>
        public Task Codesign(string file, string entitlements)
            => UseCodeSignSigning
                ? ProcessRunner.MacOSCodeSign(
                    Program.Configuration.Commands.Codesign!,
                    Program.Configuration.ConfigFiles.CodesignIdentity,
                    entitlements,
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
                    Program.Configuration.Commands.Productsign!,
                    Program.Configuration.ConfigFiles.CodesignIdentity,
                    file
                )
                : Task.CompletedTask;

    }

}
