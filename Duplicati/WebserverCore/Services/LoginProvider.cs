// Copyright (C) 2026, The Duplicati Team
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
using Duplicati.Library.Logging;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Middlewares;

namespace Duplicati.WebserverCore.Services;

public class LoginProvider(ITokenFamilyStore repo, IJWTTokenProvider tokenProvider, JWTConfig jwtConfig, Connection connection) : ILoginProvider
{
    private static readonly string LOGTAG = Log.LogTagFromType<LoginProvider>();

    public async Task<(string AccessToken, string RefreshToken, string? Nonce)> PerformLoginWithSigninTokenAsync(string signinTokenString, bool shortLived, CancellationToken ct)
    {
        var signinToken = tokenProvider.ReadSigninToken(signinTokenString);
        var userId = signinToken.UserId;

        var tokenFamily = await repo.CreateTokenFamilyAsync(userId, ct);
        var (refreshToken, nonce) = tokenProvider.CreateRefreshToken(userId, tokenFamily.Id, tokenFamily.Counter, shortLived);

        return (
            tokenProvider.CreateAccessToken(userId, tokenFamily.Id),
            refreshToken,
            nonce
        );
    }

    public async Task<(string AccessToken, string RefreshToken, string? Nonce)> PerformLoginWithRefreshTokenAsync(string refreshTokenString, string? nonce, CancellationToken ct)
    {
        var refreshToken = tokenProvider.ReadRefreshToken(refreshTokenString, nonce);
        var tokenFamily = await repo.GetTokenFamilyAsync(refreshToken.UserId, refreshToken.TokenFamilyId, ct)
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
            await repo.InvalidateTokenFamilyAsync(tokenFamily.UserId, tokenFamily.Id, ct);
            throw new UnauthorizedException("Token family re-use detected");
        }

        tokenFamily = await repo.IncrementTokenFamilyAsync(tokenFamily, ct);
        var isShortLived = (refreshToken.Expiration - refreshToken.ValidFrom).TotalMinutes <= jwtConfig.RefreshTokenShortLivedDurationInMinutes + 1;
        (var newRefreshToken, var newNonce) = tokenProvider.CreateRefreshToken(refreshToken.UserId, tokenFamily.Id, tokenFamily.Counter, isShortLived);

        return (
            tokenProvider.CreateAccessToken(refreshToken.UserId, tokenFamily.Id),
            newRefreshToken,
            newNonce
        );
    }

    public async Task<(string AccessToken, string RefreshToken, string? Nonce)> PerformLoginWithPasswordAsync(string password, bool shortLived, CancellationToken ct)
    {
        if (!connection.ApplicationSettings.VerifyWebserverPassword(password))
            throw new UnauthorizedException("Invalid password");

        var userId = "webserver";
        var tokenFamily = await repo.CreateTokenFamilyAsync(userId, ct);
        var (refreshToken, nonce) = tokenProvider.CreateRefreshToken(userId, tokenFamily.Id, tokenFamily.Counter, shortLived);

        return (
            tokenProvider.CreateAccessToken(userId, tokenFamily.Id),
            refreshToken,
            nonce
        );
    }

    public async Task PerformLogoutWithRefreshTokenAsync(string refreshTokenString, string? nonce, CancellationToken ct)
    {
        var token = tokenProvider.ReadRefreshToken(refreshTokenString, nonce);
        await repo.InvalidateTokenFamilyAsync(token.UserId, token.TokenFamilyId, ct);
    }

    public async Task PerformCompleteLogoutAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            await repo.InvalidateAllTokensAsync(ct);
        else
            await repo.InvalidateAllTokenFamiliesAsync(userId, ct);
    }
}
