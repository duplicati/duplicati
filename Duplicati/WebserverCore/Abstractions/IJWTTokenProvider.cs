namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Provides methods to create and read JWT tokens.
/// </summary>
public interface IJWTTokenProvider
{
    /// <summary>
    /// Represents a JWT token that can be used for a single operation.
    /// </summary>
    /// <param name="ValidFrom">The time the token was created.</param>
    /// <param name="Expiration">The time the token expires.</param>
    /// <param name="UserId">The user ID the token is for.</param>    
    /// <param name="Operation">The operation the token is for.</param>
    public record SingleOperationToken(DateTimeOffset ValidFrom, DateTimeOffset Expiration, string UserId, string Operation);
    /// <summary>
    /// Represents a JWT token that can be used to sign in, instead of using a password.
    /// </summary>
    /// <param name="ValidFrom">The time the token was created.</param>
    /// <param name="Expiration">The time the token expires.</param>
    /// <param name="UserId">The user ID the token is for.</param>    
    public record SigninToken(DateTimeOffset ValidFrom, DateTimeOffset Expiration, string UserId);
    /// <summary>
    /// Represents a JWT token that can be used to access resources.
    /// </summary>
    /// <param name="ValidFrom">The time the token was created.</param>
    /// <param name="Expiration">The time the token expires.</param>
    /// <param name="TokenFamilyId">The token family ID the token is for.</param>
    /// <param name="UserId">The user ID the token is for.</param>
    public record AccessToken(DateTimeOffset ValidFrom, DateTimeOffset Expiration, string TokenFamilyId, string UserId);
    /// <summary>
    /// Represents a JWT token that can be used to refresh an access token.
    /// </summary>
    /// <param name="ValidFrom">The time the token was created.</param>
    /// <param name="Expiration">The time the token expires.</param>
    /// <param name="TokenFamilyId">The token family ID the token is for.</param>
    /// <param name="UserId">The user ID the token is for.</param>
    /// <param name="Counter">The counter of the token family the token is for.</param>
    public record RefreshToken(DateTimeOffset ValidFrom, DateTimeOffset Expiration, string TokenFamilyId, string UserId, int Counter);

    /// <summary>
    /// Creates a JWT token that only works for a single operation.
    /// </summary>
    /// <param name="userId">The user ID the token is for.</param>
    /// <param name="operation">The operation the token is for.</param>
    /// <returns>The JWT token.</returns>
    string CreateSingleOperationToken(string userId, string operation);
    /// <summary>
    /// Creates a JWT token that can be used to sign in, instead of using a password.
    /// </summary>
    /// <param name="userId">The user ID the token is for.</param>
    /// <returns>The JWT token.</returns>
    string CreateSigninToken(string userId);
    /// <summary>
    /// Creates a JWT token that can be used to access resources.
    /// </summary>
    /// <param name="userId">The user ID the token is for.</param>
    /// <param name="tokenFamilyId">The token family ID the token is for.</param>
    /// <param name="expiration">The expiration time of the token, can only be shorter than the current.</param>
    /// <returns>The JWT token.</returns>
    string CreateAccessToken(string userId, string tokenFamilyId, TimeSpan? expiration = null);
    /// <summary>
    /// Creates a JWT token that can be used to access resources &quot;forever&quot;.
    /// </summary>
    /// <returns>The JWT token.</returns>
    string CreateForeverToken();
    /// <summary>
    /// Creates a JWT token that can be used to refresh an access token.
    /// </summary>
    /// <param name="userId">The user ID the token is for.</param>
    /// <param name="tokenFamilyId">The token family ID the token is for.</param>
    /// <param name="counter">The counter of the token family the token is for.</param>
    /// <returns>The JWT token.</returns>
    string CreateRefreshToken(string userId, string tokenFamilyId, int counter);

    /// <summary>
    /// Reads a JWT token that only works for a single operation.
    /// </summary>
    /// <param name="token">The JWT token.</param>
    /// <returns>The parsed and validated single operation token.</returns>
    SingleOperationToken ReadSingleOperationToken(string token);
    /// <summary>
    /// Reads a JWT token that can be used to sign in, instead of using a password.
    /// </summary>
    /// <param name="token">The JWT token.</param>
    /// <returns>The parsed and validated sign-in token.</returns>    
    SigninToken ReadSigninToken(string token);

    /// <summary>
    /// Reads a JWT token that can be used to access resources.
    /// </summary>
    /// <param name="token">The JWT token.</param>
    /// <returns>The parsed and validated access token.</returns>
    AccessToken ReadAccessToken(string token);

    /// <summary>
    /// Reads a JWT token that can be used to refresh an access token.
    /// </summary>
    /// <param name="token">The JWT token.</param>
    /// <returns>The parsed and validated refresh token.</returns>
    RefreshToken ReadRefreshToken(string token);

    /// <summary>
    /// Gets the family ID from a JWT token with no family counter.
    /// </summary>
    string TemporaryFamilyId { get; }
}
