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

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Provides methods for handling notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Gets all notifications.
    /// </summary>
    /// <returns>An enumerable collection of notification DTOs.</returns>
    IEnumerable<Dto.NotificationDto> GetNotifications();

    /// <summary>
    /// Gets a specific notification by its ID.
    /// </summary>
    /// <param name="id">The ID of the notification to retrieve.</param>
    /// <returns>The notification DTO corresponding to the specified ID.</returns>
    Dto.NotificationDto GetNotification(long id);

    /// <summary>
    /// Deletes a notification by its ID.
    /// </summary>
    void DeleteNotification(long id);
}
