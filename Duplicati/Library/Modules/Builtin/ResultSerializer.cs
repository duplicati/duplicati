using System;
using System.Collections.Generic;
using System.IO;

namespace Duplicati.Library.Modules.Builtin
{
    class ResultSerializer
    {
        public static void SerializeResult(string file, object result)
        {
            using (StreamWriter sw = new StreamWriter(file))
            {
                if (result == null)
                {
                    sw.WriteLine("null?");
                }
                else if (result is System.Collections.IEnumerable)
                {
                    System.Collections.IEnumerable ie = (System.Collections.IEnumerable)result;
                    System.Collections.IEnumerator ien = ie.GetEnumerator();
                    ien.Reset();

                    while (ien.MoveNext())
                    {
                        object c = ien.Current;
                        if (c == null)
                            continue;

                        if (c.GetType().IsGenericType && !c.GetType().IsGenericTypeDefinition && c.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                        {
                            object key = c.GetType().GetProperty("Key").GetValue(c, null);
                            object value = c.GetType().GetProperty("Value").GetValue(c, null);
                            sw.WriteLine("{0}: {1}", key, value);
                        }
                        else
                            sw.WriteLine(c);
                    }
                }
                else if (result.GetType().IsArray)
                {
                    Array a = (Array)result;

                    for (int i = a.GetLowerBound(0); i <= a.GetUpperBound(0); i++)
                    {
                        object c = a.GetValue(i);

                        if (c == null)
                            continue;

                        if (c.GetType().IsGenericType && !c.GetType().IsGenericTypeDefinition && c.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                        {
                            object key = c.GetType().GetProperty("Key").GetValue(c, null);
                            object value = c.GetType().GetProperty("Value").GetValue(c, null);
                            sw.WriteLine("{0}: {1}", key, value);
                        }
                        else
                            sw.WriteLine(c);
                    }
                }
                else if (result is Exception)
                {
                    //No localization, must be parseable by script
                    Exception e = (Exception)result;
                    sw.WriteLine("Failed: {0}", e.Message);
                    sw.WriteLine("Details: {0}", e);
                }
                else
                {
                    Utility.Utility.PrintSerializeObject(result, sw);
                }
            }
        }
    }
}
