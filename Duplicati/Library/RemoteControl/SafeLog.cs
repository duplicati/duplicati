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

using Duplicati.Library.Logging;

namespace Duplicati.Library.RemoteControl;

/// <summary>
/// Helper for writing log messages from places where an exception must never escape.
/// The log destinations are not guaranteed to be exception free; writing to a full
/// Windows event log or a broken console pipe throws, and the exception is passed on
/// to the caller of <see cref="Log.WriteMessage(LogMessageType, string, string, Exception, string, object[])"/>.
/// Inside a reactive handler such an exception detaches the subscription and is passed
/// into the websocket client, which permanently breaks the connection handling,
/// and inside a periodic loop it would terminate the loop.
/// </summary>
internal static class SafeLog
{
    /// <summary>
    /// Writes a log message, ignoring any failures from the log destinations
    /// </summary>
    /// <param name="type">The message type</param>
    /// <param name="tag">The log tag</param>
    /// <param name="id">The message id</param>
    /// <param name="ex">The exception to log, if any</param>
    /// <param name="message">The message to log</param>
    /// <param name="arguments">The arguments to format the message with</param>
    public static void Write(LogMessageType type, string tag, string id, Exception? ex, string message, params object?[] arguments)
    {
        try
        {
            Log.WriteMessage(type, tag, id, ex!, message, arguments!);
        }
        catch
        {
            // Logging must never break the caller
        }
    }
}
