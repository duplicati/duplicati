using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend.Box;

/// <summary>
/// An error reported by the Box.com API, carrying the parsed error response so
/// callers can react to a specific error without matching on the message
/// </summary>
public sealed class BoxException : UserInformationException
{
    /// <summary>
    /// The error reported by Box.com
    /// </summary>
    public ErrorResponse Error { get; }

    /// <summary>
    /// Creates a new exception for a Box.com error
    /// </summary>
    /// <param name="error">The parsed error response</param>
    /// <param name="innerException">The exception that reported the failed request</param>
    public BoxException(ErrorResponse error, Exception innerException)
        : base($"Box.com ErrorResponse: {error.Status} - {error.Code}: {error.Message}", "box.com", innerException)
    {
        Error = error;
    }
}
