namespace Duplicati.WebserverCore.Abstractions;

public interface ICommandlineRunService
{
    public IActiveRun? GetActiveRun(string id);

    string StartTask(string[] args);

    public interface IActiveRun
    {
        string ID { get; }
        DateTime LastAccess { get; }
        object Lock { get; }

        bool Finished { get; }
        bool Started { get; }
        bool IsLogDisposed { get; }

        IEnumerable<string> GetLog();

        void Abort();

    }
}
