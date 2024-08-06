namespace Duplicati.WebserverCore.Exceptions;

public class BadRequestException(string? Message = null) : UserReportedHttpException(Message ?? "Bad Request")
{
    public override int StatusCode => 400;
}
