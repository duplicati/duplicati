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
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Provides functionality to manage and retrieve information about tasks in the task queue.
/// </summary>
/// <param name="connection">The database connection used to access task information.</param>
public class NotificationService(Connection connection) : INotificationService
{
    /// <inheritdoc/>
    public void DeleteNotification(long id)
    {
        var notification = connection.GetNotifications().FirstOrDefault(n => n.ID == id);
        if (notification == null)
            throw new NotFoundException("Notification not found");

        connection.DismissNotification(id);
    }

    /// <inheritdoc/>
    public NotificationDto GetNotification(long id)
    {
        var notification = connection.GetNotifications().FirstOrDefault(n => n.ID == id);
        if (notification == null)
            throw new NotFoundException("Notification not found");

        return NotificationDto.FromEntity(notification);
    }

    /// <inheritdoc/>
    public IEnumerable<NotificationDto> GetNotifications()
        => connection.GetNotifications().Select(notification => NotificationDto.FromEntity(notification));
}