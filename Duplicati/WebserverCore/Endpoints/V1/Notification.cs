// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

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
