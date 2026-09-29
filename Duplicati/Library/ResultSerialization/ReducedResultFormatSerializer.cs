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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Logging;
using Newtonsoft.Json.Linq;

namespace Duplicati.Library.ResultSerialization
{
    /// <summary>
    /// Wraps any result serializer so the serialized result carries no text: the result is
    /// replaced by its reduced view (see <see cref="ReducedResultView"/>) and the exception by
    /// its type name and help id. Log lines and extra data are passed through unchanged, since
    /// the report modules reduce those at capture time.
    /// </summary>
    public sealed class ReducedResultFormatSerializer : IResultFormatSerializer
    {
        /// <summary>
        /// The serializer that renders the reduced view
        /// </summary>
        private readonly IResultFormatSerializer m_inner;

        /// <summary>
        /// Creates a reduced serializer around an existing one
        /// </summary>
        /// <param name="inner">The serializer that renders the reduced view</param>
        public ReducedResultFormatSerializer(IResultFormatSerializer inner)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc />
        public ResultExportFormat Format => m_inner.Format;

        /// <inheritdoc />
        public string Serialize(object result, Exception exception, IEnumerable<string> loglines, Dictionary<string, string> additional)
            => m_inner.Serialize(
                ReducedResultView.Create(result),
                exception == null ? null : new ReducedReportException(exception),
                loglines,
                additional);
    }

    /// <summary>
    /// An exception whose message and text are the reduced form of another exception: the type
    /// name and help id only. Passed to the inner serializers in place of the real exception, so
    /// every format renders the same reduced text without knowing about reduced mode.
    /// </summary>
    public sealed class ReducedReportException : Exception
    {
        /// <summary>
        /// The reduced text
        /// </summary>
        private readonly string m_text;

        /// <summary>
        /// Creates the reduced form of an exception
        /// </summary>
        /// <param name="source">The exception to reduce</param>
        public ReducedReportException(Exception source)
            : base(ReducedReportFormat.FormatException(source ?? throw new ArgumentNullException(nameof(source))))
        {
            m_text = Message;
        }

        /// <inheritdoc />
        public override string ToString() => m_text;
    }

    /// <summary>
    /// Builds the reduced view of a result object: numbers, booleans, dates, durations and a
    /// short allowlist of named strings are kept, every other string (messages, warnings,
    /// errors, paths, verification lists) is dropped at any depth. The view is a plain
    /// dictionary tree, which every serializer can render.
    /// </summary>
    public static class ReducedResultView
    {
        /// <summary>
        /// String-valued properties that survive the reduction. Mirrors the receivers' allowlist.
        /// </summary>
        public static readonly IReadOnlySet<string> AllowedStringProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "MainOperation",
            "ParsedResult",
            "Mode",
            "Version",
            "EncryptionModule",
            "CompressionModule"
        };

        /// <summary>
        /// A serialized date-time, which carries no report content
        /// </summary>
        private static readonly Regex DateTimePattern = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// A serialized duration, which carries no report content
        /// </summary>
        private static readonly Regex DurationPattern = new Regex(@"^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Creates the reduced view of a result. A null result stays null; an exception used as
        /// the result is reduced to its type name and help id.
        /// </summary>
        /// <param name="result">The result to reduce</param>
        /// <returns>The reduced view</returns>
        public static object Create(object result)
        {
            if (result == null)
                return null;

            if (result is Exception exception)
                return new ReducedReportException(exception);

            // Serialize the way the JSON report does, so the same properties are considered,
            // then strip on the token tree rather than on the typed object
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result, new Newtonsoft.Json.JsonSerializerSettings
            {
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
                ContractResolver = new ResultContractResolver(),
                Converters = new List<Newtonsoft.Json.JsonConverter> { new Newtonsoft.Json.Converters.StringEnumConverter() }
            });
            var token = JToken.Parse(json);
            return Reduce(token);
        }

        /// <summary>
        /// Leaves out the task control members and task-typed properties of the result classes,
        /// as the JSON report serializer does
        /// </summary>
        private sealed class ResultContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            private static readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.Ordinal) { "TaskReader", "TaskControl" };

            protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(Type type, Newtonsoft.Json.MemberSerialization memberSerialization)
                => base.CreateProperties(type, memberSerialization)
                    .Where(x => !Excluded.Contains(x.PropertyName))
                    .Where(x => !typeof(System.Threading.Tasks.Task).IsAssignableFrom(x.PropertyType))
                    .ToList();
        }

        /// <summary>
        /// Returns whether a string value is a date-time or duration
        /// </summary>
        /// <param name="value">The value</param>
        /// <returns><c>true</c> when the value carries no report content</returns>
        public static bool IsDateOrDurationValue(string value)
            => value != null && (DateTimePattern.IsMatch(value) || DurationPattern.IsMatch(value));

        /// <summary>
        /// Reduces a token tree to a dictionary/list tree of allowed values
        /// </summary>
        /// <param name="token">The token to reduce</param>
        /// <returns>The reduced value, or null when nothing survives</returns>
        private static object Reduce(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in obj.Properties())
                    {
                        if (prop.Value is JValue value)
                        {
                            if (value.Type == JTokenType.String)
                            {
                                var text = value.Value<string>();
                                if (AllowedStringProperties.Contains(prop.Name) || IsDateOrDurationValue(text))
                                    dict[prop.Name] = text;
                                continue;
                            }

                            dict[prop.Name] = ScalarValue(value);
                            continue;
                        }

                        dict[prop.Name] = Reduce(prop.Value);
                    }
                    return dict;

                case JArray array:
                    var list = new List<object>();
                    foreach (var item in array)
                    {
                        if (item is JValue value)
                        {
                            if (value.Type == JTokenType.String)
                            {
                                // Strings in arrays are messages, warnings, errors or paths; only dates survive
                                var text = value.Value<string>();
                                if (IsDateOrDurationValue(text))
                                    list.Add(text);
                                continue;
                            }

                            list.Add(ScalarValue(value));
                            continue;
                        }

                        list.Add(Reduce(item));
                    }
                    return list;

                case JValue rootValue:
                    return rootValue.Type == JTokenType.String
                        ? (IsDateOrDurationValue(rootValue.Value<string>()) ? rootValue.Value<string>() : null)
                        : ScalarValue(rootValue);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns the CLR value of a non-string scalar token, with dates rendered the way the JSON report renders them
        /// </summary>
        /// <param name="value">The token</param>
        /// <returns>The value</returns>
        private static object ScalarValue(JValue value)
        {
            if (value.Type == JTokenType.Date && value.Value is DateTime dateTime)
                return dateTime.ToString("o");
            if (value.Type == JTokenType.TimeSpan && value.Value is TimeSpan timeSpan)
                return timeSpan.ToString();
            return value.Value;
        }
    }
}
