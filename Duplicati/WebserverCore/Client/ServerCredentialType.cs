namespace Duplicati.WebserverCore.Client;

/// <summary>
/// Specifies the type of credential used for authenticating with the Duplicati server.
/// </summary>
public enum ServerCredentialType
{
    /// <summary>
    /// Use password-based authentication. The credential should be the server's webserver password.
    /// This will perform a login operation via the /api/v1/auth/login endpoint.
    /// </summary>
    Password,

    /// <summary>
    /// Use token-based authentication. The credential should be a signin token obtained from the server.
    /// This will perform a signin operation via the /api/v1/auth/signin endpoint.
    /// </summary>
    Token
}
