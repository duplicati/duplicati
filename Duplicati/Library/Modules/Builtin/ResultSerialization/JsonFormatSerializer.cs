using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Duplicati.Library.Modules.Builtin.ResultSerialization
{
    /// <summary>
    /// Implements serialization of results to JSON
    /// </summary>
    public class JsonFormatSerializer : IResultFormatSerializer
    {
        /// <summary>
        /// Helper to filter the result classes properties
        /// </summary>
        private class DynamicContractResolver : DefaultContractResolver
        {
            /// <summary>
            /// List of names to exclude
            /// </summary>
            private readonly HashSet<string> m_excludes;
            /// <summary>
            /// Initializes a new instance of the
            /// <see cref="T:Duplicati.Library.Modules.Builtin.SendHttpMessage.DynamicContractResolver"/> class.
            /// </summary>
            /// <param name="names">The names to exclude.</param>
            public DynamicContractResolver(params string[] names)
            {
                m_excludes = new HashSet<string>(names);
            }

            /// <summary>
            /// Creates a filtered list of properties
            /// </summary>
            /// <returns>The filtered properties.</returns>
            /// <param name="type">The type to create the list for.</param>
            /// <param name="memberSerialization">Member serialization parameter.</param>
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                return base
                    .CreateProperties(type, memberSerialization)
                    .Where(x => !m_excludes.Contains(x.PropertyName))
                    .Where(x => !typeof(Task).IsAssignableFrom(x.PropertyType))
                    .ToList();
            }
        }

        /// <summary>
        /// Serialize the specified result and logLines.
        /// </summary>
        /// <returns>The serialized result string.</returns>
        /// <param name="result">The result to serialize.</param>
        /// <param name="loglines">The log lines to serialize.</param>
        /// <param name="additional">Additional parameters to include</param>
        public string Serialize(object result, IEnumerable<string> loglines, Dictionary<string, string> additional)
        {
            return JsonConvert.SerializeObject(
                new
                {
                    Data = result,
                    Extra = additional,
                    LogLines = loglines
                }, 
                
                new JsonSerializerSettings()
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    ContractResolver = new DynamicContractResolver(
                            nameof(IBasicResults.Warnings),
                            nameof(IBasicResults.Errors),
                            nameof(IBasicResults.Messages),
                            "TaskReader"
                    ),
                    Converters = new List<JsonConverter>()
                    {
                        new StringEnumConverter()
                    }
                }
            );
        }

        /// <summary>
        /// Returns the format that the serializer represents
        /// </summary>
        public ResultExportFormat Format => ResultExportFormat.Json;
    }
}
