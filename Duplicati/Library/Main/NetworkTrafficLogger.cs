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

#nullable enable

using System;
using System.Diagnostics.Tracing;
using System.Text;

namespace Duplicati.Library.Main;

/// <summary>
/// Listener for HTTP diagnostics events
/// This listener captures events from System.Net.Http and System.Net.Sockets
/// and forwards them to the Duplicati logging system.
/// </summary>
/// <param name="enableHttpLogging">If true, enables logging of HTTP events.</param>
/// <param name="socketDataBytes">The number of bytes of socket data to log, or -1 to disable logging of socket data.</param>
internal sealed class NetworkTrafficLogger(bool enableHttpLogging, int socketDataBytes) : EventListener
{
    /// <summary>
    /// Log tag for HTTP events
    /// </summary>
    private static readonly string LogTagHttp = Logging.Log.LogTagFromType<NetworkTrafficLogger>() + ".Http";
    /// <summary>
    /// Log tag for socket events
    /// </summary>
    private static readonly string LogTagSocket = Logging.Log.LogTagFromType<NetworkTrafficLogger>() + ".Socket";

    /// <inheritdoc/>
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if ((enableHttpLogging && eventSource.Name == "System.Net.Http") || (socketDataBytes >= 0 && eventSource.Name == "System.Net.Sockets"))
            EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
    }

    /// <inheritdoc/>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (enableHttpLogging && eventData.EventSource.Name == "System.Net.Http")
        {
            Logging.Log.WriteVerboseMessage(LogTagHttp, eventData.EventName, FormatPayload(eventData));
        }
        else if (socketDataBytes >= 0 && eventData.EventSource.Name == "System.Net.Sockets")
        {
            Logging.Log.WriteVerboseMessage(LogTagSocket, eventData.EventName, FormatSocketPayload(eventData));
        }
    }

    /// <summary>
    /// Formats the payload of an HTTP event for logging.
    /// </summary>
    /// <param name="eventData">The event data containing the payload.</param>
    /// <returns>A formatted string representation of the payload.</returns>
    private static string FormatPayload(EventWrittenEventArgs eventData)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{eventData.EventSource.Name}] Event: {eventData.EventName}");

        if (eventData.Payload != null && eventData.Payload.Count > 0)
        {
            for (int i = 0; i < eventData.Payload.Count; i++)
            {
                var name = eventData.PayloadNames?[i] ?? $"Field{i}";
                var value = FormatValue(eventData.Payload[i]);
                sb.AppendLine($"  {name,-20}: {value}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats the payload of a socket event for logging.
    /// </summary>
    /// <param name="eventData">The event data containing the payload.</param>
    /// <returns>A formatted string representation of the socket payload.</returns>
    private string FormatSocketPayload(EventWrittenEventArgs eventData)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{eventData.EventSource.Name}] Event: {eventData.EventName}");

        if (eventData.Payload != null && eventData.Payload.Count > 0)
        {
            for (int i = 0; i < eventData.Payload.Count; i++)
            {
                var name = eventData.PayloadNames?[i] ?? $"Field{i}";
                var val = eventData.Payload[i];

                if (val is byte[] bytes)
                {
                    int len = socketDataBytes == 0 ? 0 : Math.Min(bytes.Length, socketDataBytes);
                    if (len > 0)
                    {
                        string hex = BitConverter.ToString(bytes, 0, len);
                        string ascii = Encoding.ASCII.GetString(bytes, 0, len).Replace('\0', '.');
                        sb.AppendLine($"  {name,-20}: {hex} (ASCII: {ascii})");
                    }
                    else
                    {
                        sb.AppendLine($"  {name,-20}: [Byte array suppressed]");
                    }
                }
                else
                {
                    sb.AppendLine($"  {name,-20}: {FormatValue(val)}");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a value for logging.
    /// </summary>
    /// <param name="val">The value to format.</param>
    /// <returns>A formatted string representation of the value.</returns>
    private static string FormatValue(object? val)
    {
        if (val == null)
            return "(null)";
        var type = val.GetType();

        if (type.IsEnum)
            return $"{val} ({Convert.ToInt32(val)})";

        return val.ToString() ?? string.Empty;
    }
}
