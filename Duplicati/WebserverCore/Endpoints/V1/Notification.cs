
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Notification : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/notification/{id}", ([FromRoute] long id, [FromServices] Connection connection)
            => ExecuteGet(id, connection))
            .RequireAuthorization();

        group.MapGet("/notifications", ([FromServices] Connection connection)
            => ExecuteGet(connection))
            .RequireAuthorization();

        group.MapDelete("/notification/{id}", ([FromRoute] long id, [FromServices] Connection connection)
            => ExecuteDelete(id, connection))
            .RequireAuthorization();
    }

    private static Dto.NotificationDto FromEntity(INotification notification)
        => new Dto.NotificationDto()
        {
            ID = notification.ID,
            Type = notification.Type,
            Message = notification.Message,
            Title = notification.Title,
            Exception = notification.Exception,
            BackupID = notification.BackupID,
            Action = notification.Action,
            Timestamp = notification.Timestamp,
            LogEntryID = notification.LogEntryID,
            MessageID = notification.MessageID,
            MessageLogTag = notification.MessageLogTag
        };

    private static IEnumerable<Dto.NotificationDto> ExecuteGet(Connection connection)
        => connection.GetNotifications().Select(FromEntity);

    private static Dto.NotificationDto ExecuteGet(long id, Connection connection)
    {
        var notification = connection.GetNotifications().FirstOrDefault(n => n.ID == id);
        if (notification == null)
            throw new NotFoundException("Notification not found");

        return FromEntity(notification);
    }

    private static void ExecuteDelete(long id, Connection connection)
    {
        var notification = connection.GetNotifications().FirstOrDefault(n => n.ID == id);
        if (notification == null)
            throw new NotFoundException("Notification not found");

        connection.DismissNotification(id);
    }
}
