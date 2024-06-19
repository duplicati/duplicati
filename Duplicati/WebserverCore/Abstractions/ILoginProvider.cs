namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Provides login functionality.
/// </summary>
public interface ILoginProvider
{
    /// <summary>
    /// Performs a login with a signin token.
    /// </summary>
    /// <param name="signinTokenString">The signin token.</param>
    /// <param name="issueRefreshToken">Whether to issue a refresh token.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The access and refresh tokens.</returns>
    Task<(string AccessToken, string? RefreshToken)> PerformLoginWithSigninToken(string signinTokenString, bool issueRefreshToken, CancellationToken ct);

    /// <summary>
    /// Performs a login with a refresh token.
    /// </summary>
    /// <param name="refreshTokenString">The refresh token.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The access and refresh tokens.</returns>
    Task<(string AccessToken, string RefreshToken)> PerformLoginWithRefreshToken(string refreshTokenString, CancellationToken ct);

    /// <summary>
    /// Performs a login with a password.
    /// </summary>
    /// <param name="password">The password.</param>
    /// <param name="issueRefreshToken">Whether to issue a refresh token.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The access and refresh tokens.</returns>
    Task<(string AccessToken, string? RefreshToken)> PerformLoginWithPassword(string password, bool issueRefreshToken, CancellationToken ct);

    /// <summary>
    /// Performs a logout with a refresh token.
    /// </summary>
    /// <param name="refreshTokenString">The refresh token.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The task.</returns>
    Task PerformLogoutWithRefreshToken(string refreshTokenString, CancellationToken ct);

    /// <summary>
    /// Performs a complete logout for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The task.</returns>
    Task PerformCompleteLogout(string userId, CancellationToken ct);
}