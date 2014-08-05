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

    public class SettingsCreator : CustomCreationConverter<Interface.ISetting>
    {
        public override Interface.ISetting Create(Type objectType)
        {
            return new Implementations.Setting();
        }
    }

    public class FilterCreator : CustomCreationConverter<Interface.IFilter>
    {
        public override Interface.IFilter Create(Type objectType)
        {
            return new Implementations.Filter();
        }
    }

    public class NotificationCreator : CustomCreationConverter<Interface.INotification>
    {
        public override Interface.INotification Create(Type objectType)
        {
            return new Implementations.Notification();
        }
    }

    // This class is needed for some reason, with newer versions of JSON.Net (5.0+)
    public class BasicStringEnumConverter : Newtonsoft.Json.Converters.StringEnumConverter
    {
        public override void WriteJson(JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(value.ToString());
            else
                base.WriteJson(writer, value, serializer);
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType.IsEnum;
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
            return typeof(DayOfWeek).IsAssignableFrom(objectType);
        }

        #endregion
    }
    
}
