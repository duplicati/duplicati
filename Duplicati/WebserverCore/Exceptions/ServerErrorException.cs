namespace Duplicati.WebserverCore.Exceptions;

public class ServerErrorException(string Message) : UserReportedHttpException(Message)
{
    public override int StatusCode => 500;
}
