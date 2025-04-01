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
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Duplicati.WebserverCore.Middlewares;

public record JWTConfig
{
    public required string Authority { get; init; }
    public required string Audience { get; init; }
    public required string SigningKey { get; init; }
    public int AccessTokenDurationInMinutes { get; init; } = 15;
    public int RefreshTokenDurationInMinutes { get; init; } = 60 * 24 * 30;
    public int SigninTokenDurationInMinutes { get; init; } = 5;
    public int SingleOperationTokenDurationInMinutes { get; init; } = 1;
    public int MaxRefreshTokenDrift { get; init; } = 1;
    public int MaxRefreshTokenDriftSeconds { get; init; } = 30;
    public SymmetricSecurityKey SymmetricSecurityKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public static JWTConfig Create() => new()
    {
        Authority = "https://duplicati",
        Audience = "https://duplicati",
        SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
    };
}

public class JWTTokenProvider(JWTConfig jWTConfig) : IJWTTokenProvider
{
    private const string TemporaryFamilyId = "temporary";
    private const string ForeverTokenUserId = "forever-token";
    public string CreateSingleOperationToken(string userId, string operation)
        => GenerateToken([
            new Claim(Claims.Type, TokenType.SingleOperationToken.ToString()),
            new Claim(Claims.UserId, userId),
            new Claim(Claims.Operation, operation)
        ], DateTime.Now, expires: DateTime.Now.AddMinutes(jWTConfig.SingleOperationTokenDurationInMinutes));

    public string CreateSigninToken(string userId)
        => GenerateToken([
            new Claim(Claims.Type, TokenType.SigninToken.ToString()),
            new Claim(Claims.UserId, userId)
        ], DateTime.Now, expires: DateTime.Now.AddMinutes(jWTConfig.SigninTokenDurationInMinutes));

    public string CreateAccessToken(string userId, string tokenFamilyId, TimeSpan? expiration = null)
    => GenerateToken([
            new Claim(Claims.Type, TokenType.AccessToken.ToString()),
            new Claim(Claims.UserId, userId),
            new Claim(Claims.Family, tokenFamilyId)
        ], DateTime.Now, expires: DateTime.Now.AddMinutes(Math.Min(jWTConfig.AccessTokenDurationInMinutes, expiration?.TotalMinutes ?? jWTConfig.AccessTokenDurationInMinutes)));

    public string CreateForeverToken()
    => GenerateToken([
            new Claim(Claims.Type, TokenType.AccessToken.ToString()),
            new Claim(Claims.UserId, ForeverTokenUserId),
            new Claim(Claims.Family, TemporaryFamilyId)
        ], DateTime.Now, expires: DateTime.Now.AddYears(10));

    public string CreateRefreshToken(string userId, string tokenFamilyId, int counter)
        => GenerateToken([
            new Claim(Claims.Type, TokenType.RefreshToken.ToString()),
            new Claim(Claims.UserId, userId),
            new Claim(Claims.Family, tokenFamilyId),
            new Claim(Claims.Counter, counter.ToString()),
            new Claim(Claims.IssuedAt, (DateTime.UnixEpoch - DateTime.UtcNow).TotalSeconds.ToString())
        ], DateTime.Now, expires: DateTime.Now.AddMinutes(jWTConfig.RefreshTokenDurationInMinutes));

    private string GenerateToken(IEnumerable<Claim> claims, DateTime notBefore, DateTime expires)
    {
        var creds = new SigningCredentials(jWTConfig.SymmetricSecurityKey(), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(jWTConfig.Authority, jWTConfig.Audience, claims, notBefore: notBefore, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public IJWTTokenProvider.SingleOperationToken ReadSingleOperationToken(string token)
    {
        var jwtToken = ParseAndValidateToken(token, TokenType.SingleOperationToken);

        return new IJWTTokenProvider.SingleOperationToken(
            jwtToken.ValidFrom,
            jwtToken.ValidTo,
            jwtToken.Claims.First(c => c.Type == Claims.UserId).Value,
            jwtToken.Claims.First(c => c.Type == Claims.Operation).Value
        );
    }

    public IJWTTokenProvider.SigninToken ReadSigninToken(string token)
    {
        var jwtToken = ParseAndValidateToken(token, TokenType.SigninToken);

        return new IJWTTokenProvider.SigninToken(
            jwtToken.ValidFrom,
            jwtToken.ValidTo,
            jwtToken.Claims.First(c => c.Type == Claims.UserId).Value
        );
    }

    public IJWTTokenProvider.AccessToken ReadAccessToken(string token)
    {
        var jwtToken = ParseAndValidateToken(token, TokenType.AccessToken);

        return new IJWTTokenProvider.AccessToken(
            jwtToken.ValidFrom,
            jwtToken.ValidTo,
            jwtToken.Claims.First(c => c.Type == Claims.Family).Value,
            jwtToken.Claims.First(c => c.Type == Claims.UserId).Value
        );
    }

    public IJWTTokenProvider.RefreshToken ReadRefreshToken(string token)
    {
        var jwtToken = ParseAndValidateToken(token, TokenType.RefreshToken);

        return new IJWTTokenProvider.RefreshToken(
            jwtToken.ValidFrom,
            jwtToken.ValidTo,
            jwtToken.Claims.First(c => c.Type == Claims.Family).Value,
            jwtToken.Claims.First(c => c.Type == Claims.UserId).Value,
            int.Parse(jwtToken.Claims.First(c => c.Type == Claims.Counter).Value)
        );
    }

    private JwtSecurityToken ParseAndValidateToken(string token, TokenType tokenType)
    {
        new JwtSecurityTokenHandler().ValidateToken(token, GetTokenValidationParameters(jWTConfig), out var securityToken);
        if (securityToken is JwtSecurityToken jwtToken && jwtToken.Claims.First(c => c.Type == Claims.Type).Value == tokenType.ToString())
            return jwtToken;

        throw new SecurityTokenValidationException("Invalid token type");
    }

    public static TokenValidationParameters GetTokenValidationParameters(JWTConfig jWTConfig)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jWTConfig.Authority,
            ValidAudience = jWTConfig.Audience,
            IssuerSigningKey = jWTConfig.SymmetricSecurityKey(),
            ClockSkew = TimeSpan.FromSeconds(5)
        };
    }

    public static async Task ValidateAccessToken(TokenValidatedContext context, ITokenFamilyStore store)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = context.SecurityToken as JsonWebToken ?? throw new Exception("Invalid token");

        var tokenTypeClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == Claims.Type);
        if (tokenTypeClaim == null || tokenTypeClaim.Value != TokenType.AccessToken.ToString())
        {
            context.Fail("Invalid token type.");
            return;
        }
        var tokenFamilyClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == Claims.Family);
        if (tokenFamilyClaim == null || string.IsNullOrEmpty(tokenFamilyClaim.Value))
        {
            context.Fail("Invalid token.");
            return;
        }
        var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == Claims.UserId);
        if (userIdClaim == null || string.IsNullOrEmpty(userIdClaim.Value))
        {
            context.Fail("Invalid token.");
            return;
        }

        if (tokenFamilyClaim.Value != TemporaryFamilyId)
        {
            var tokenFamily = await store.GetTokenFamily(userIdClaim.Value, tokenFamilyClaim.Value, context.HttpContext.RequestAborted);
            if (tokenFamily == null)
            {
                context.Fail("Invalid token.");
                return;
            }
        }
    }

    string IJWTTokenProvider.TemporaryFamilyId => TemporaryFamilyId;

    private enum TokenType
    {
        AccessToken,
        RefreshToken,
        SigninToken,
        SingleOperationToken
    }

    private static class Claims
    {
        public const string Type = "typ";
        public const string UserId = "sid";
        public const string Family = "fam";
        public const string Counter = "cnt";
        public const string IssuedAt = "iat";
        public const string Operation = "sop";
    }

}

