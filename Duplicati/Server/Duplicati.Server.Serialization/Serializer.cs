// Copyright (C) 2024, The Duplicati Team
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Duplicati.Server.Serialization
{    
    public class Serializer
    {
        protected static readonly JsonSerializerSettings m_jsonSettings;
        protected static readonly Formatting m_jsonFormatting = Formatting.Indented;

        static Serializer()
        {
            m_jsonSettings = new JsonSerializerSettings();
            m_jsonSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
            m_jsonSettings.Converters = new JsonConverter[] {
                new DayOfWeekConcerter(),
                new StringEnumConverter(),
                new SerializableStatusCreator(),
                new SettingsCreator(),
                new FilterCreator(),
                new NotificationCreator(),
            }.ToList();
        }

        public static void SerializeJson(System.IO.TextWriter sw, object o, bool preventDispose = false)
        {
            Newtonsoft.Json.JsonSerializer jsonSerializer = Newtonsoft.Json.JsonSerializer.Create(m_jsonSettings);
            var jsonWriter = new JsonTextWriter(sw);
            using (preventDispose ? null : jsonWriter)
            {
                jsonWriter.Formatting = m_jsonFormatting;
                jsonSerializer.Serialize(jsonWriter, o);
                jsonWriter.Flush();
                sw.Flush();
            }
        }

        public static async Task SerializeJsonAsync(System.IO.TextWriter tw, object o, bool preventDispose = false)
        {
            Newtonsoft.Json.JsonSerializer jsonSerializer = Newtonsoft.Json.JsonSerializer.Create(m_jsonSettings);
            StringBuilder sb = new StringBuilder();
            StringWriter sw = new StringWriter(sb);
            var jsonWriter = new JsonTextWriter(sw);
            using (preventDispose ? null : jsonWriter)
            {
                jsonWriter.Formatting = m_jsonFormatting;
                jsonSerializer.Serialize(jsonWriter, o); 
                await jsonWriter.FlushAsync();
            }
            await sw.FlushAsync();
            await tw.WriteAsync(sb.ToString());
            await tw.FlushAsync();
        }

        public static T Deserialize<T>(System.IO.TextReader sr)
        {
            Newtonsoft.Json.JsonSerializer jsonSerializer = Newtonsoft.Json.JsonSerializer.Create(m_jsonSettings);
            using (var jsonReader = new JsonTextReader(sr))
            {
                jsonReader.Culture = System.Globalization.CultureInfo.InvariantCulture;
                return jsonSerializer.Deserialize<T>(jsonReader);
            }
        }
    }
}
