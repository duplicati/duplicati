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
/// Represents a store for token families.
/// </summary>
public interface ITokenFamilyStore
{
    /// <summary>
    /// Gets a token family.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="familyId">The family ID.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The token family.</returns>
    Task<TokenFamily> GetTokenFamily(string userId, string familyId, CancellationToken ct);
    /// <summary>
    /// Creates a token family.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The token family.</returns>
    Task<TokenFamily> CreateTokenFamily(string userId, CancellationToken ct);
    /// <summary>
    /// Invalidates a token family.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="familyId">The family
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The task.</returns>
    Task InvalidateTokenFamily(string userId, string familyId, CancellationToken ct);
    /// <summary>
    /// Invalidates all token families for a given userId.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The task.</returns>
    Task InvalidateAllTokenFamilies(string userId, CancellationToken ct);
    /// <summary>
    /// Invalidates all token families.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    Task InvalidateAllTokens(CancellationToken ct);
    /// <summary>
    /// Increments a token family.
    /// </summary>
    /// <param name="tokenFamily">The token family.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The incremented token family.</returns>
    Task<TokenFamily> IncrementTokenFamily(TokenFamily tokenFamily, CancellationToken ct);

    /// <summary>
    /// Represents a token family.
    /// </summary>
    /// <param name="Id">The ID.</param>
    /// <param name="UserId">The user ID.</param>
    /// <param name="Counter">The counter.</param>  
    /// <param name="LastUpdated">The last updated timestamp.</param>
    public record TokenFamily(string Id, string UserId, int Counter, DateTime LastUpdated);
}
