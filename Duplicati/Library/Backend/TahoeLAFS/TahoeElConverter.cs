using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Duplicati.Library.Backend;

internal class TahoeElConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => objectType == typeof(TahoeEl);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var array = JArray.Load(reader);
        string? nodetype = null;
        TahoeNode? node = null;
        foreach (var token in array.Children())
            switch (token.Type)
            {
                case JTokenType.String:
                    nodetype = token.ToString();
                    break;
                case JTokenType.Object:
                    node = token.ToObject<TahoeNode>(serializer);
                    break;
            }

        return new TahoeEl { Nodetype = nodetype, Node = node };
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}