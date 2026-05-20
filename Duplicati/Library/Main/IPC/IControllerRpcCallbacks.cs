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

using System.Threading.Tasks;
using Duplicati.Library.Main.IPC.Dto;

namespace Duplicati.Library.Main.IPC;

/// <summary>
/// Callback interface for server to call client (events)
/// </summary>
public interface IControllerRpcCallbacks
{
    // Log message forwarding
    Task OnLogMessageAsync(LogEntryDto entry);

    // Progress updates
    Task OnBackendEventAsync(BackendActionType action, BackendEventType type, string path, long size);
    Task OnBackendProgressAsync(BackendProgressDto progress);
    Task OnOperationProgressAsync(OperationProgressDto progress);
    Task OnPhaseChangedAsync(OperationPhase phase, OperationPhase previousPhase);

    // Operation lifecycle
    Task OnOperationStartedAsync(OperationMode operation);
    Task OnOperationCompletedAsync(OperationResultDto result, ExceptionDto exception);
}
