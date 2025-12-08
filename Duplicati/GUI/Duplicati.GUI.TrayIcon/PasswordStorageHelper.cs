using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;

#nullable enable

namespace Duplicati.GUI.TrayIcon;

public class PasswordStorageHelper
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<PasswordStorageHelper>();
    private const string HOSTURL_SECRET_NAME = "duplicati-trayicon-hosturl";
    private const string PASSWORD_SECRET_NAME = "duplicati-trayicon-password";

    private readonly ISecretProvider? m_secretProvider;
    private string m_hostUrl;
    private string m_password;
    private readonly TaskCompletionSource<bool> m_passwordUpdatedTcs = new TaskCompletionSource<bool>();
    private readonly Program.PasswordSource m_passwordSource;

    public event EventHandler? OnPasswordChanged;

    public bool IsSetSupported => m_secretProvider?.IsSetSupported ?? false;

    public string? HostUrl => m_hostUrl;
    public string? Password => m_password;

    public bool ShouldSavePassword { get; private set; }

    public bool NeedsPasswordPrompt => m_passwordSource == Program.PasswordSource.SuppliedPassword && string.IsNullOrWhiteSpace(m_password);

    public static async Task<PasswordStorageHelper> CreateAsync(string? hostUrl, bool customUrl, string? password, Program.PasswordSource passwordSource, Dictionary<string, string?> options)
    {
        var secretProvider = await SecretProviderHelper.GetDefaultSecretProvider(options, CancellationToken.None);

        // If we get the password from the secret provider, we should save it back to the secret provider
        var shouldSavePassword = false;

        if (secretProvider != null && passwordSource == Program.PasswordSource.SuppliedPassword && (string.IsNullOrWhiteSpace(hostUrl) || !customUrl || string.IsNullOrWhiteSpace(password)))
        {
            if (string.IsNullOrWhiteSpace(hostUrl) || !customUrl)
            {
                try
                {
                    hostUrl = await secretProvider.ResolveSecretAsync(HOSTURL_SECRET_NAME, CancellationToken.None).ConfigureAwait(false);
                    shouldSavePassword = true;
                }
                catch (Exception ex)
                {
                    Library.Logging.Log.WriteInformationMessage(LOGTAG, "SecretProviderFailedToGetEncryptionKey", $"Failed to get stored configuration \"{HOSTURL_SECRET_NAME}\": {ex.Message}");

                }
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                try
                {
                    password = await secretProvider.ResolveSecretAsync(PASSWORD_SECRET_NAME, CancellationToken.None).ConfigureAwait(false);
                    shouldSavePassword = true;
                }
                catch (Exception ex)
                {
                    Library.Logging.Log.WriteInformationMessage(LOGTAG, "SecretProviderFailedToGetEncryptionKey", $"Failed to get stored configuration \"{PASSWORD_SECRET_NAME}\": {ex.Message}");
                }
            }
        }

        return new PasswordStorageHelper(secretProvider, hostUrl, password, passwordSource, shouldSavePassword);
    }

    private PasswordStorageHelper(ISecretProvider? secretProvider, string? hostUrl, string? password, Program.PasswordSource passwordSource, bool shouldSavePassword)
    {
        m_secretProvider = secretProvider;
        m_hostUrl = hostUrl ?? string.Empty;
        m_password = password ?? string.Empty;
        m_passwordSource = passwordSource;
        ShouldSavePassword = shouldSavePassword;
        if (!string.IsNullOrWhiteSpace(m_password))
            m_passwordUpdatedTcs.TrySetResult(true);
    }

    public Task WaitForPasswordUpdateAsync(CancellationToken cancellationToken)
        => m_passwordUpdatedTcs.Task.WaitAsync(cancellationToken);

    public async Task<bool> UpdatePasswordAsync(string? hostUrl, string? password, bool save, CancellationToken cancellationToken)
    {
        var res = false;
        if (m_secretProvider != null && IsSetSupported && save)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hostUrl) && hostUrl != m_hostUrl)
                {
                    await m_secretProvider.SetSecretAsync(
                        HOSTURL_SECRET_NAME,
                        hostUrl ?? string.Empty,
                        true,
                        cancellationToken).ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(password) && password != m_password)
                {
                    await m_secretProvider.SetSecretAsync(
                        PASSWORD_SECRET_NAME,
                        password ?? string.Empty,
                        true,
                        cancellationToken).ConfigureAwait(false);
                }

                res = true;
                ShouldSavePassword = true;
            }
            catch (Exception ex)
            {
                Library.Logging.Log.WriteWarningMessage(LOGTAG, "PasswordSaveFailed", ex, "Failed to save configuration");
            }
        }

        m_hostUrl = hostUrl ?? string.Empty;
        m_password = password ?? string.Empty;
        m_passwordUpdatedTcs.TrySetResult(true);
        OnPasswordChanged?.Invoke(this, EventArgs.Empty);

        return res;
    }
}
