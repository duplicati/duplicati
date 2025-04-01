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