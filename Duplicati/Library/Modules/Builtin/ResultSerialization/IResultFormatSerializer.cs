using System.Collections.Generic;

namespace Duplicati.Library.Modules.Builtin
{
    /// <summary>
    /// Interface for describing a result serializer
    /// </summary>
    public interface IResultFormatSerializer
    {
        /// <summary>
        /// Serialize the specified result and logLines.
        /// </summary>
        /// <returns>The serialized result string.</returns>
        /// <param name="result">The result to serialize.</param>
        /// <param name="loglines">The log lines to serialize.</param>
        string Serialize(object result, IEnumerable<string> loglines);

        /// <summary>
        /// Returns the format that the serializer represents
        /// </summary>
        ResultExportFormat Format { get; }
    }
}
