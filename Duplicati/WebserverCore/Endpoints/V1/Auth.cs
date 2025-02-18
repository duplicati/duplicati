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

using Duplicati.Library.Logging;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Middlewares;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Duplicati.WebserverCore.Endpoints.V1;

public partial class Auth : IEndpointV1
{
    private static readonly string LOGTAG = Log.LogTagFromType<Auth>();

    private const string COOKIE_NAME = "RefreshToken";

    private static string GetCookieName(IHttpContextAccessor httpContextAccessor)
        => $"{COOKIE_NAME}_{httpContextAccessor.HttpContext?.Request.Host.Port ?? 0}";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("auth/refresh", async ([FromServices] ILoginProvider loginProvider, [FromServices] JWTConfig jWTConfig, [FromServices] IHttpContextAccessor httpContextAccessor, CancellationToken ct) =>
        {
            var cookieName = GetCookieName(httpContextAccessor);
            if (httpContextAccessor.HttpContext!.Request.Cookies.TryGetValue(cookieName, out var refreshTokenString))
            {
                try
                {
                    var result = await loginProvider.PerformLoginWithRefreshToken(refreshTokenString, ct);
                    AddCookie(httpContextAccessor.HttpContext, cookieName, result.RefreshToken, DateTimeOffset.UtcNow.AddMinutes(jWTConfig.RefreshTokenDurationInMinutes));
                    return new Dto.AccessTokenOutput(result.AccessToken);
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "RefreshTokenError", ex, "Failed to refresh token");
                    // Any error with the refresh token should be treated as unauthorized
                    throw new UnauthorizedException("Failed to refresh token");
                }
            }

            throw new UnauthorizedException("Authorization failed due to missing cookie.");
        });

        group.MapPost("auth/signin", async ([FromServices] ILoginProvider loginProvider, [FromServices] JWTConfig jWTConfig, [FromServices] IHttpContextAccessor httpContextAccessor, [FromServices] Connection connection, [FromBody] Dto.SigninInputDto input, CancellationToken ct) =>
        {
            if (connection.ApplicationSettings.DisableSigninTokens)
                throw new UnauthorizedException("Signin tokens are disabled");

            var cookieName = GetCookieName(httpContextAccessor);
            try
            {
                var result = await loginProvider.PerformLoginWithSigninToken(input.SigninToken, input.RememberMe ?? false, ct);
                if (!string.IsNullOrWhiteSpace(result.RefreshToken))
                    AddCookie(httpContextAccessor.HttpContext!, cookieName, result.RefreshToken, DateTimeOffset.UtcNow.AddMinutes(jWTConfig.RefreshTokenDurationInMinutes));
                return new Dto.AccessTokenOutput(result.AccessToken);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "SigninTokenError", ex, "Failed to sign in");

                // Any error with the signin token should be treated as unauthorized
                if (ex is SecurityTokenExpiredException)
                    throw new UnauthorizedException("Signin token expired");

                throw new UnauthorizedException("Failed to sign in");
            }
        });

        group.MapPost("auth/login", async ([FromServices] ILoginProvider loginProvider, [FromServices] JWTConfig jWTConfig, [FromServices] IHttpContextAccessor httpContextAccessor, [FromBody] Dto.LoginInputDto input, CancellationToken ct) =>
        {
            var cookieName = GetCookieName(httpContextAccessor);
            try
            {
                var result = await loginProvider.PerformLoginWithPassword(input.Password, input.RememberMe ?? false, ct);
                if (!string.IsNullOrWhiteSpace(result.RefreshToken))
                    AddCookie(httpContextAccessor.HttpContext!, cookieName, result.RefreshToken, DateTimeOffset.UtcNow.AddMinutes(jWTConfig.RefreshTokenDurationInMinutes));
                return new Dto.AccessTokenOutput(result.AccessToken);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "PasswordLoginError", ex, "Failed to log in");
                // Any error with the password should be treated as unauthorized
                throw new UnauthorizedException("Failed to log in");
            }
        });

        group.MapPost("auth/issuesignintoken", ([FromServices] Connection connection, [FromServices] IJWTTokenProvider tokenProvider, [FromBody] Dto.IssueSigninTokenInputDto input) =>
        {
            if (connection.ApplicationSettings.DisableSigninTokens)
                throw new UnauthorizedException("Signin tokens are disabled");

            if (!connection.ApplicationSettings.VerifyWebserverPassword(input.Password))
                throw new UnauthorizedException("Incorrect password");

            var signinToken = tokenProvider.CreateSigninToken("web-api");
            return new Dto.SigninTokenOutputDto(signinToken);
        });

        group.MapPost("auth/refresh/logout", ([FromServices] ILoginProvider loginProvider, [FromServices] IHttpContextAccessor httpContextAccessor) =>
            PerformLogout(loginProvider, httpContextAccessor));

        group.MapPost("auth/issuetoken/{operation}", ([FromServices] Connection connection, [FromServices] IJWTTokenProvider tokenProvider, [FromRoute] string operation) =>
        {
            switch (operation)
            {
                case "export":
                case "bugreport":
                    break;

                default:
                    throw new BadRequestException("Invalid operation");
            }
            var singleOperationToken = tokenProvider.CreateSingleOperationToken("web-api", operation);
            return new Dto.SingleOperationTokenOutputDto(singleOperationToken);
        }).RequireAuthorization();

        group.MapPost("auth/issue-forever-token", ([FromServices] Connection connection, [FromServices] IJWTTokenProvider tokenProvider) =>
        {
            var res = connection.ApplicationSettings.ConsumeForeverToken();
            if (res == null)
                throw new UnauthorizedException("Forever tokens are not enabled");
            if (!res.Value)
                throw new UnauthorizedException("Cannot generate multiple forever tokens, restart the server to generate a new one");

            return new Dto.AccessTokenOutput(tokenProvider.CreateForeverToken());
        }).RequireAuthorization();
    }

    private static void AddCookie(HttpContext context, string name, string value, DateTimeOffset expires)
        => context.Response.Cookies.Append(name, value, new CookieOptions
        {
            Expires = expires,
            Path = "/api/v1/auth/refresh",
            Secure = context.Request.IsHttps,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Domain = context.Request.Host.Host
        });

    private static object PerformLogout(ILoginProvider loginProvider, IHttpContextAccessor httpContextAccessor)
    {
        var cookieName = GetCookieName(httpContextAccessor);
        if (httpContextAccessor.HttpContext!.Request.Cookies.TryGetValue(cookieName, out var refreshTokenString))
        {
            try
            {
                loginProvider.PerformLogoutWithRefreshToken(refreshTokenString, CancellationToken.None);
            }
            catch
            {
                // Ignore invalid refresh tokens
            }
        }

        // Also remove the cookie, in case we failed to delete it
        httpContextAccessor.HttpContext!.Response.Cookies.Delete(cookieName);
        return new { success = true };
    }

}
