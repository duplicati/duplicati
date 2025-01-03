namespace Duplicati.WebserverCore.Exceptions;

public class InvalidHostnameException(string? Hostname = null) : UserReportedHttpException($"Invalid hostname: {Hostname}")
{
    public override int StatusCode => 403;
}