namespace Duplicati.WebserverCore.Exceptions
{
    public abstract class UserReportedHttpException(string? Message = null)
        : Exception(Message ?? "Error")
    {
        public abstract int StatusCode { get; }
        public virtual string ContentType => "application/json";
    }
}