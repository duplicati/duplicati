namespace Duplicati.WebserverCore.Options;

public class NotificationsOptions
{
    public const string SectionName = "Duplicati:Notifications";

    public string WebsocketPath { get; set; } = "notifications";
}