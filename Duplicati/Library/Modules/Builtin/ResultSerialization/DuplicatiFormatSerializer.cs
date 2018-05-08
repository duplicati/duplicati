using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Modules.Builtin.ResultSerialization
{
    /// <summary>
    /// Provides a human-readable output for result data
    /// </summary>
    public class DuplicatiFormatSerializer : IResultFormatSerializer
    {
        /// <summary>
        /// Serialize the specified result and logLines.
        /// </summary>
        /// <returns>The serialized result string.</returns>
        /// <param name="result">The result to serialize.</param>
        /// <param name="loglines">The log lines to serialize.</param>
        /// <param name="additional">Additional parameters to include</param>
        public string Serialize(object result, IEnumerable<string> loglines, Dictionary<string, string> additional)
        {
            StringBuilder sb = new StringBuilder();

            if (result == null)
            {
                sb.Append("null?");
            }
            else if (result is IEnumerable)
            {
                IEnumerable resultEnumerable = (IEnumerable)result;
                IEnumerator resultEnumerator = resultEnumerable.GetEnumerator();
                resultEnumerator.Reset();

                while (resultEnumerator.MoveNext())
                {
                    object current = resultEnumerator.Current;
                    if (current == null)
                    {
                        continue;
                    }

                    if (current.GetType().IsGenericType && !current.GetType().IsGenericTypeDefinition && current.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    {
                        object key = current.GetType().GetProperty("Key").GetValue(current, null);
                        object value = current.GetType().GetProperty("Value").GetValue(current, null);
                        sb.AppendFormat("{0}: {1}", key, value).AppendLine();
                    }
                    else
                    {
                        sb.AppendLine(current.ToString());
                    }
                }
            }
            else if (result.GetType().IsArray)
            {
                Array array = (Array)result;

                for (int i = array.GetLowerBound(0); i <= array.GetUpperBound(0); i++)
                {
                    object c = array.GetValue(i);

                    if (c == null)
                    {
                        continue;
                    }

                    if (c.GetType().IsGenericType && !c.GetType().IsGenericTypeDefinition && c.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    {
                        object key = c.GetType().GetProperty("Key").GetValue(c, null);
                        object value = c.GetType().GetProperty("Value").GetValue(c, null);
                        sb.AppendFormat("{0}: {1}", key, value).AppendLine();
                    }
                    else
                    {
                        sb.AppendLine(c.ToString());
                    }
                }
            }
            else if (result is Exception)
            {
                //No localization, must be parseable by script
                Exception exception = (Exception)result;
                sb.AppendFormat("Failed: {0}", exception.Message).AppendLine();
                sb.AppendFormat("Details: {0}", exception).AppendLine();
            }
            else
            {
                var ignore = new string[] { 
                    nameof(IBasicResults.Warnings),
                    nameof(IBasicResults.Errors),
                    nameof(IBasicResults.Messages)
                };

                Utility.Utility.PrintSerializeObject(result, sb, (p, o) => !ignore.Contains(p.Name));
            }

            if (additional != null && additional.Count > 0)
                sb.AppendLine(Serialize(additional, null, null));

            if (loglines != null && loglines.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Log data:");
                foreach (var n in loglines)
                    sb.AppendLine(n);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns the format that the serializer represents
        /// </summary>
        public ResultExportFormat Format => ResultExportFormat.Duplicati;
    }
}
