namespace Duplicati.WebserverCore.Exceptions
{
    public class ConflictException(string Message) : UserReportedHttpException(Message ?? "Conflict")
    {
        public override int StatusCode => 409;
    }
}