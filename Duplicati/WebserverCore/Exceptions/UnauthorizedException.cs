namespace Duplicati.WebserverCore.Exceptions;

public class UnauthorizedException(string Message) : UserReportedHttpException(Message)
{
    public override int StatusCode => 401;
}
