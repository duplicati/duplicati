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
