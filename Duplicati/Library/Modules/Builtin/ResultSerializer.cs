using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Duplicati.Library.Modules.Builtin
{
    class ResultSerializer
    {
        public static void SerializeToFile(string filename, object result)
        {
            using (StreamWriter sw = new StreamWriter(filename))
            {
                if (result == null)
                {
                    sw.WriteLine("null?");
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
                            sw.WriteLine("{0}: {1}", key, value);
                        }
                        else
                        {
                            sw.WriteLine(current);
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
                            sw.WriteLine("{0}: {1}", key, value);
                        }
                        else
                        {
                            sw.WriteLine(c);
                        }
                    }
                }
                else if (result is Exception)
                {
                    //No localization, must be parseable by script
                    Exception exception = (Exception)result;
                    sw.WriteLine("Failed: {0}", exception.Message);
                    sw.WriteLine("Details: {0}", exception);
                }
                else
                {
                    Utility.Utility.PrintSerializeObject(result, sw);
                }
            }
        }
    }
}
