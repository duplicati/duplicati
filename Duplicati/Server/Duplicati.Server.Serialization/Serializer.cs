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
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Duplicati.Server.Serialization
{
    public class Serializer
    {
        public static JsonSerializerSettings JsonSettings { get; }
        protected static readonly Formatting m_jsonFormatting = Formatting.Indented;

        static Serializer()
        {
            JsonSettings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Converters = new JsonConverter[]
                {
                    new DayOfWeekConcerter(),
                    new StringEnumConverter(),
                    new SettingsCreator(),
                    new FilterCreator(),
                    new NotificationCreator(),
                }.ToList()
            };
        }

        public static void SerializeJson(System.IO.TextWriter sw, object o, bool preventDispose = false)
        {
            JsonSerializer jsonSerializer = JsonSerializer.Create(JsonSettings);
            var jsonWriter = new JsonTextWriter(sw);
            using (preventDispose ? null : jsonWriter)
            {
                jsonWriter.Formatting = m_jsonFormatting;
                jsonSerializer.Serialize(jsonWriter, o);
                jsonWriter.Flush();
                sw.Flush();
            }
        }

        public static T Deserialize<T>(System.IO.TextReader sr)
        {
            JsonSerializer jsonSerializer = JsonSerializer.Create(JsonSettings);
            using (var jsonReader = new JsonTextReader(sr))
            {
                jsonReader.Culture = System.Globalization.CultureInfo.InvariantCulture;
                return jsonSerializer.Deserialize<T>(jsonReader);
            }
        }
    }
}
