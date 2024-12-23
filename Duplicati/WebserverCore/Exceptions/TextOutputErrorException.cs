
namespace Duplicati.WebserverCore.Exceptions;

public class TextOutputErrorException(string message, int statusCode = 200, string contentType = "text/plain") : UserReportedHttpException(message)
{
    public override int StatusCode => statusCode;
    public override string ContentType => contentType;
}
