namespace Duplicati.WebserverCore.Exceptions
{
    public class ServiceUnavailableException(string Message) : UserReportedHttpException(Message)
    {
        public override int StatusCode => 503;
    }
}