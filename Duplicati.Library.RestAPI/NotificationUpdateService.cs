namespace Duplicati.Library.RestAPI;

public interface INotificationUpdateService
{
    /// <summary>
    /// An event ID that increases whenever the database is updated
    /// </summary>
    long LastDataUpdateId { get; }

    /// <summary>
    /// An event ID that increases whenever a notification is updated
    /// </summary>
    long LastNotificationUpdateId { get; }

    void IncrementLastDataUpdateId();
    void IncrementLastNotificationUpdateId();
}

public class NotificationUpdateService : INotificationUpdateService
{
    /// <summary>
    /// An event ID that increases whenever the database is updated
    /// </summary>
    public long LastDataUpdateId { get; private set; } = 0;

    private readonly object _lastDataUpdateIdLock = new();

    /// <summary>
    /// An event ID that increases whenever a notification is updated
    /// </summary>
    public long LastNotificationUpdateId { get; private set; } = 0;

    private readonly object _lastNotificationUpdateIdLock = new();

    public void IncrementLastDataUpdateId()
    {
        lock (_lastDataUpdateIdLock)
        {
            LastDataUpdateId++;
        }
    }    
    
    public void IncrementLastNotificationUpdateId()
    {
        lock (_lastNotificationUpdateIdLock)
        {
            LastNotificationUpdateId++;
        }
    }
}