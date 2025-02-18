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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duplicati.WebserverCore;

public class DayOfWeekStringEnumConverter : JsonConverterFactory
{
    private class DayOfWeekConverter : JsonConverter<DayOfWeek>
    {
        public override DayOfWeek Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException();

            return reader.GetString()?.Trim()?.ToLowerInvariant() switch
            {
                "sun" => DayOfWeek.Sunday,
                "mon" => DayOfWeek.Monday,
                "tue" => DayOfWeek.Tuesday,
                "wed" => DayOfWeek.Wednesday,
                "thu" => DayOfWeek.Thursday,
                "fri" => DayOfWeek.Friday,
                "sat" => DayOfWeek.Saturday,
                _ => throw new JsonException()
            };
        }

        public override void Write(Utf8JsonWriter writer, DayOfWeek value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case DayOfWeek.Sunday:
                    writer.WriteStringValue("sun");
                    break;
                case DayOfWeek.Monday:
                    writer.WriteStringValue("mon");
                    break;
                case DayOfWeek.Tuesday:
                    writer.WriteStringValue("tue");
                    break;
                case DayOfWeek.Wednesday:
                    writer.WriteStringValue("wed");
                    break;
                case DayOfWeek.Thursday:
                    writer.WriteStringValue("thu");
                    break;
                case DayOfWeek.Friday:
                    writer.WriteStringValue("fri");
                    break;
                case DayOfWeek.Saturday:
                    writer.WriteStringValue("sat");
                    break;
                default:
                    throw new JsonException();
            }
            writer.WriteStringValue(value.ToString());
        }
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(DayOfWeek);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new DayOfWeekConverter();
    }
}
