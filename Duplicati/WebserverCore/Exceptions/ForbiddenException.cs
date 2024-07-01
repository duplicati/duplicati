namespace Duplicati.WebserverCore.Exceptions;
public class ForbiddenException(string Message) : UserReportedHttpException(Message)
{
    public override int StatusCode => 403;
}
