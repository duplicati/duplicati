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
