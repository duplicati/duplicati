using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            m_jsonSettings.ContractResolver = new JsonSerializer();
            m_jsonSettings.Converters = new JsonConverter[] {
                new BasicStringEnumConverter(),
                new SerializableStatusCreator(),
                new SettingsCreator(),
                new FilterCreator(),
                new NotificationCreator(),
                new DayOfWeekConcerter(),
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
