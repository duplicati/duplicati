// Copyright (C) 2026, The Duplicati Team
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

using Duplicati.Server.Serialization;

namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The request to set the action that is performed when the task queue has run empty
/// </summary>
/// <param name="Action">The action to perform, where <see cref="QueueCompletionAction.None"/> cancels a pending action</param>
public sealed record QueueCompletionActionInputDto(QueueCompletionAction Action);

/// <summary>
/// The action that is performed when the task queue has run empty, and the actions the server can perform
/// </summary>
/// <param name="PendingAction">The action that is performed when the task queue has run empty</param>
/// <param name="SupportedActions">The actions the server is allowed to perform on the computer it runs on</param>
public sealed record QueueCompletionActionDto(QueueCompletionAction PendingAction, IReadOnlyList<QueueCompletionAction> SupportedActions);
