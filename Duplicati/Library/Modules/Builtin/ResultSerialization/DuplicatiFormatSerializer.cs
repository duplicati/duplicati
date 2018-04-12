using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Modules.Builtin.ResultSerialization
{
    class DuplicatiFormatSerializer : IResultFormatSerializer
    {
        public string Serialize(object result)
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
                Utility.Utility.PrintSerializeObject(result, sb);
            }

            return sb.ToString();
        }
    }
}
