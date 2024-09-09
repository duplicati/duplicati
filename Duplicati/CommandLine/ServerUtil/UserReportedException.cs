namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// An exception that should be reported to the user.
/// </summary>
/// <param name="message">The message of the exception</param>
/// <param name="innerException">The inner exception</param>
[Serializable]
public class UserReportedException(string message, Exception? innerException = null) : Exception(message, innerException);
