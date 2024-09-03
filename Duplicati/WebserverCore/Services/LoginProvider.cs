using Duplicati.Library.Logging;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Middlewares;

namespace Duplicati.WebserverCore.Services;

public class LoginProvider(ITokenFamilyStore repo, IJWTTokenProvider tokenProvider, JWTConfig jwtConfig, Connection connection) : ILoginProvider
{
    private static readonly string LOGTAG = Log.LogTagFromType<LoginProvider>();

    public async Task<(string AccessToken, string? RefreshToken)> PerformLoginWithSigninToken(string signinTokenString, bool issueRefreshToken, CancellationToken ct)
    {
        var signinToken = tokenProvider.ReadSigninToken(signinTokenString);
        var userId = signinToken.UserId;
        if (!issueRefreshToken)
            return (tokenProvider.CreateAccessToken(userId, tokenProvider.TemporaryFamilyId), null);

        var tokenFamily = await repo.CreateTokenFamily(userId, ct);

        return (
            tokenProvider.CreateAccessToken(userId, tokenFamily.Id),
            tokenProvider.CreateRefreshToken(userId, tokenFamily.Id, tokenFamily.Counter)
        );
    }

    public async Task<(string AccessToken, string RefreshToken)> PerformLoginWithRefreshToken(string refreshTokenString, CancellationToken ct)
    {
        var refreshToken = tokenProvider.ReadRefreshToken(refreshTokenString);
        var tokenFamily = await repo.GetTokenFamily(refreshToken.UserId, refreshToken.TokenFamilyId, ct)
            ?? throw new UnauthorizedException("Invalid refresh token");

        // Allow slight drift to adjust for cases where the browser refreshes
        // just before the token is received, so the server is ahead
        var counterDiff = tokenFamily.Counter - refreshToken.Counter;
        var maxDrift = (DateTime.UtcNow - tokenFamily.LastUpdated).TotalSeconds > jwtConfig.MaxRefreshTokenDriftSeconds
            ? 0
            : jwtConfig.MaxRefreshTokenDrift;
        if (counterDiff < 0 || counterDiff > maxDrift)
        {
            Log.WriteWarningMessage(LOGTAG, "TokenFamilyReuse", null, $"Invalid refresh token counter: {tokenFamily.Counter} != {refreshToken.Counter}");
            await repo.InvalidateTokenFamily(tokenFamily.UserId, tokenFamily.Id, ct);
            throw new UnauthorizedException("Token family re-use detected");
        }

        tokenFamily = await repo.IncrementTokenFamily(tokenFamily, ct);

        return (
            tokenProvider.CreateAccessToken(refreshToken.UserId, tokenFamily.Id),
            tokenProvider.CreateRefreshToken(refreshToken.UserId, tokenFamily.Id, tokenFamily.Counter)
        );
    }

    public async Task<(string AccessToken, string? RefreshToken)> PerformLoginWithPassword(string password, bool issueRefreshToken, CancellationToken ct)
    {
        if (!connection.ApplicationSettings.VerifyWebserverPassword(password))
            throw new UnauthorizedException("Invalid password");

        var userId = "webserver";
        if (!issueRefreshToken)
            return (tokenProvider.CreateAccessToken(userId, tokenProvider.TemporaryFamilyId), null);

        var tokenFamily = await repo.CreateTokenFamily(userId, ct);

        return (
            tokenProvider.CreateAccessToken(userId, tokenFamily.Id),
            tokenProvider.CreateRefreshToken(userId, tokenFamily.Id, tokenFamily.Counter)
        );
    }

    public async Task PerformLogoutWithRefreshToken(string refreshTokenString, CancellationToken ct)
    {
        var token = tokenProvider.ReadRefreshToken(refreshTokenString);
        await repo.InvalidateTokenFamily(token.UserId, token.TokenFamilyId, ct);
    }

    public async Task PerformCompleteLogout(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            await repo.InvalidateAllTokens(ct);
        else
            await repo.InvalidateAllTokenFamilies(userId, ct);
    }
}
