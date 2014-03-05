using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

namespace Duplicati.Server.Serialization
{
    public class SerializableStatusCreator : CustomCreationConverter<Interface.IServerStatus>
    {
        public override Interface.IServerStatus Create(Type objectType)
        {
            return new Implementations.ServerStatus();
        }
    }

    public class ProgressEventDataCreator : CustomCreationConverter<Interface.IProgressEventData>
    {
        public override Interface.IProgressEventData Create(Type objectType)
        {
            return new Implementations.ProgressEventData();
        }
    }

    public class SettingsCreator : CustomCreationConverter<Interface.ISetting>
    {
        public override Interface.ISetting Create(Type objectType)
        {
            return new Implementations.Setting();
        }
    }

    public class DayOfWeekConcerter : JsonConverter
    {
        #region implemented abstract members of JsonConverter

        public override void WriteJson(JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (value == null)
            {
                throw new JsonSerializationException(string.Format("Invalid DayOfWeek: {0}", value));
            }
            else
            {
                DayOfWeek v = (DayOfWeek)value;
            
                switch ((DayOfWeek)value)
                {
                    case DayOfWeek.Monday:
                        writer.WriteValue("mon");
                        break;
                    case DayOfWeek.Tuesday:
                        writer.WriteValue("tue");
                        break;
                    case DayOfWeek.Wednesday:
                        writer.WriteValue("wed");
                        break;
                    case DayOfWeek.Thursday:
                        writer.WriteValue("thu");
                        break;
                    case DayOfWeek.Friday:
                        writer.WriteValue("fri");
                        break;
                    case DayOfWeek.Saturday:
                        writer.WriteValue("sat");
                        break;
                    case DayOfWeek.Sunday:
                        writer.WriteValue("sun");
                        break;
                    default:
                        throw new JsonSerializationException(string.Format("Invalid DayOfWeek: {0}", value));
                }
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                throw new JsonSerializationException(string.Format("Cannot convert null value to {0}", objectType));
            else if (reader.TokenType != JsonToken.String)
                throw new JsonSerializationException(string.Format("Cannot convert {0} value to {1}", reader.TokenType, objectType));
            
            var v = (string)reader.Value;
            DayOfWeek result;
            if (Enum.TryParse(v, out result))
                return result;
            
            switch (v.ToLowerInvariant())
            {
                case "mon":
                    return DayOfWeek.Monday;
                case "tue":
                    return DayOfWeek.Tuesday;
                case "wed":
                    return DayOfWeek.Wednesday;
                case "thu":
                    return DayOfWeek.Thursday;
                case "fri":
                    return DayOfWeek.Friday;
                case "sat":
                    return DayOfWeek.Saturday;
                case "sun":
                    return DayOfWeek.Sunday;                    
            }
            
            throw new JsonSerializationException(string.Format("Cannot convert \"{0}\" to {1}", v, objectType));
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(DayOfWeek);
        }

        #endregion
    }
    
}
