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
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Duplicati.Library.Logging
{
    /// <summary>
    /// The reduced report format: what a report may contain when the machine processes confidential or protected information.
    /// A reduced report carries log message ids and numbers only, never message text, format
    /// arguments, exception messages or paths. The receivers (console ingress, activity monitor)
    /// validate against the same rules, so any change here must be mirrored there.
    /// </summary>
    public static class ReducedReportFormat
    {
        /// <summary>
        /// The option that switches every report module to the reduced format.
        /// </summary>
        public const string OPTION_REDUCED_REPORTING = "reduced-reporting";

        /// <summary>
        /// The key in the report's extra data that carries the report mode.
        /// </summary>
        public const string REPORT_MODE_KEY = "report-mode";

        /// <summary>
        /// The report mode value for a reduced report.
        /// </summary>
        public const string REDUCED_REPORT_MODE = "reduced";

        /// <summary>
        /// Formats a reduced log line: the log timestamp and the <c>[Level-Tag-Id]</c> filter tag,
        /// followed by the exception type in a second bracket group when the entry carried one.
        /// The shape matches <see cref="LogEntry.ToString"/> without the message part.
        /// </summary>
        /// <param name="when">The time of the log entry</param>
        /// <param name="filterTag">The filter tag, <c>Level-Tag-Id</c></param>
        /// <param name="exceptionTypeName">The exception type name, or null when the entry carried no exception</param>
        /// <returns>The reduced log line</returns>
        public static string FormatLogLine(DateTime when, string filterTag, string? exceptionTypeName)
        {
            // Invariant culture: the receivers expect ':' between the time parts, while the current
            // culture may use another separator (Danish uses '.')
            var line = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss zz} - [{1}]", when.ToLocalTime(), SanitizeToken(filterTag));
            if (!string.IsNullOrWhiteSpace(exceptionTypeName))
                line += $" [ex:{SanitizeToken(exceptionTypeName)}]";
            return line;
        }

        /// <summary>
        /// Formats a reduced log line: the log timestamp and the <c>[Level-Tag-Id]</c> filter tag,
        /// followed by the exception chain when the entry carried an exception.
        /// </summary>
        /// <param name="when">The time of the log entry</param>
        /// <param name="filterTag">The filter tag, <c>Level-Tag-Id</c></param>
        /// <param name="exception">The exception, or null when the entry carried none</param>
        /// <returns>The reduced log line</returns>
        public static string FormatLogLine(DateTime when, string filterTag, Exception? exception)
        {
            var sb = new StringBuilder(FormatLogLine(when, filterTag, (string?)null));
            var depth = 0;
            for (var current = exception; current != null && depth < MAX_EXCEPTION_CHAIN; current = current.InnerException, depth++)
            {
                sb.Append(" [ex:").Append(ExceptionTypeName(current)).Append(']');
                AppendStack(sb, current);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Formats a reduced log line from a log entry.
        /// </summary>
        /// <param name="entry">The log entry</param>
        /// <returns>The reduced log line</returns>
        public static string FormatLogLine(LogEntry entry)
            => FormatLogLine(entry.When, entry.FilterTag, entry.Exception);

        /// <summary>
        /// Formats a reduced exception: the exception type name, followed by the help id when the
        /// exception is a <c>UserInformationException</c>, the stack frames, and the same for each
        /// inner exception. No message, no parameters, no file names.
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <returns>The reduced exception text</returns>
        public static string FormatException(Exception exception)
        {
            var sb = new StringBuilder(ExceptionTypeName(exception));
            var helpId = GetHelpId(exception);
            if (!string.IsNullOrWhiteSpace(helpId))
                sb.Append(" [helpid:").Append(SanitizeToken(helpId)).Append(']');
            AppendStack(sb, exception);

            var depth = 1;
            for (var current = exception.InnerException; current != null && depth < MAX_EXCEPTION_CHAIN; current = current.InnerException, depth++)
            {
                sb.Append(" [ex:").Append(ExceptionTypeName(current)).Append(']');
                AppendStack(sb, current);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The most stack frames reported for one exception
        /// </summary>
        public const int MAX_STACK_FRAMES = 10;

        /// <summary>
        /// The most exceptions reported for one chain: the exception and its inner exceptions
        /// </summary>
        public const int MAX_EXCEPTION_CHAIN = 5;

        /// <summary>
        /// The name of the state machine type the compiler generates for an async or iterator method
        /// </summary>
        private static readonly Regex StateMachineTypeName = new Regex(@"^<(?<method>[^>]+)>d__\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Returns the stack frames of an exception as <c>Namespace.Type.Method</c>, innermost first.
        /// The frames are built from the method metadata and not from the exception's own stack
        /// trace text, which an exception can override, so they hold nothing but names from the code:
        /// no parameters, no file names, no line numbers.
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <returns>The frames, at most <see cref="MAX_STACK_FRAMES"/></returns>
        public static IReadOnlyList<string> FormatStackFrames(Exception exception)
        {
            var result = new List<string>();
            StackFrame[] frames;
            try { frames = new StackTrace(exception, fNeedFileInfo: false).GetFrames(); }
            catch { return result; }

            foreach (var frame in frames)
            {
                if (result.Count >= MAX_STACK_FRAMES)
                    break;

                var name = FormatFrame(frame);
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }

            return result;
        }

        /// <summary>
        /// Formats one frame, or returns null when the frame has no method to name
        /// </summary>
        /// <param name="frame">The frame</param>
        /// <returns>The frame name</returns>
        private static string? FormatFrame(StackFrame frame)
        {
            try
            {
                var method = frame.GetMethod();
                if (method == null)
                    return null;

                var type = method.DeclaringType;
                var methodName = method.Name;

                // Report an async or iterator method by its own name rather than the generated state machine
                if (type != null && type.DeclaringType != null)
                {
                    var match = StateMachineTypeName.Match(type.Name);
                    if (match.Success)
                    {
                        methodName = match.Groups["method"].Value;
                        type = type.DeclaringType;
                    }
                }

                var typeName = type == null ? null : TypeName(type);
                return SanitizeFrame(typeName == null ? methodName : $"{typeName}.{methodName}");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The full name of a type without generic argument lists
        /// </summary>
        /// <param name="type">The type</param>
        /// <returns>The name</returns>
        private static string TypeName(Type type)
        {
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
                type = type.GetGenericTypeDefinition();
            return type.FullName ?? type.Name;
        }

        /// <summary>
        /// Keeps the characters the receivers accept in a frame and drops everything else,
        /// including generic argument lists
        /// </summary>
        /// <param name="frame">The frame</param>
        /// <returns>The sanitized frame</returns>
        private static string SanitizeFrame(string frame)
        {
            var bracket = frame.IndexOf('[');
            if (bracket >= 0)
                frame = frame.Substring(0, bracket);

            return new string(frame.Where(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                || c == '_' || c == '.' || c == '+' || c == '`' || c == '<' || c == '>' || c == '|' || c == '$').ToArray());
        }

        /// <summary>
        /// Appends the stack group of an exception, when it has frames
        /// </summary>
        /// <param name="sb">The builder to append to</param>
        /// <param name="exception">The exception</param>
        private static void AppendStack(StringBuilder sb, Exception exception)
        {
            var frames = FormatStackFrames(exception);
            if (frames.Count > 0)
                sb.Append(" [st:").Append(string.Join(';', frames)).Append(']');
        }

        /// <summary>
        /// Returns the exception type name in a form the reduced format accepts: the full type
        /// name without generic argument lists, which contain brackets and spaces.
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <returns>The type name</returns>
        public static string? ExceptionTypeName(Exception exception)
            => ExceptionTypeNameFromText(exception.GetType().FullName ?? exception.GetType().Name);

        /// <summary>
        /// Extracts the exception type name from the start of an exception's text representation,
        /// which begins with the type name followed by a colon or whitespace. Used where only the
        /// text of the exception is available.
        /// </summary>
        /// <param name="exceptionText">The exception text, typically <c>Exception.ToString()</c></param>
        /// <returns>The type name, or null when the text is empty</returns>
        public static string? ExceptionTypeNameFromText(string? exceptionText)
        {
            if (string.IsNullOrWhiteSpace(exceptionText))
                return null;

            var end = exceptionText.IndexOfAny([':', ' ', '\t', '\r', '\n', '[']);
            var name = end < 0 ? exceptionText : exceptionText.Substring(0, end);
            name = name.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : SanitizeToken(name);
        }

        /// <summary>
        /// Returns the help id of an exception, when it carries one.
        /// The interface type lives in another assembly, so the property is read by name.
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <returns>The help id, or null</returns>
        public static string? GetHelpId(Exception exception)
        {
            var type = exception.GetType();
            var value = type.GetField("HelpID")?.GetValue(exception) as string
                ?? type.GetProperty("HelpID")?.GetValue(exception) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Removes every character that would make a token unacceptable to the receivers:
        /// whitespace, colons and brackets.
        /// </summary>
        /// <param name="token">The token</param>
        /// <returns>The sanitized token</returns>
        private static string SanitizeToken(string token)
            => new string(token.Where(c => !char.IsWhiteSpace(c) && c != ':' && c != '[' && c != ']').ToArray());
    }
}
