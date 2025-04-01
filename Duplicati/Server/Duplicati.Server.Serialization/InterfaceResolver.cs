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
using System;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

namespace Duplicati.Server.Serialization
{
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
