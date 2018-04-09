using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Duplicati.Library.Modules.Builtin.ResultSerialization
{
    class JsonFormatSerializer : IResultFormatSerializer
    {
        public string Serialize(object result)
        {
            return JsonConvert.SerializeObject(result, new JsonSerializerSettings()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Converters = new List<JsonConverter>()
                {
                    new StringEnumConverter()
                }
            });
        }
    }
}
