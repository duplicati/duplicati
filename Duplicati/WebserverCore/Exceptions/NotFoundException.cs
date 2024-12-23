namespace Duplicati.WebserverCore.Exceptions;

public class NotFoundException(string? Message) : UserReportedHttpException(Message ?? "Not found")
{
    public override int StatusCode => 404;
}
