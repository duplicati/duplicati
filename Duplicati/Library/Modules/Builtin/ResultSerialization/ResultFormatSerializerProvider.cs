using System;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Modules.Builtin.ResultSerialization
{
    /// <summary>
    /// Factory class to provide result serialization
    /// </summary>
    public static class ResultFormatSerializerProvider
    {
        public static IResultFormatSerializer GetSerializer(ResultExportFormat format) {
            switch (format)
            {
                case ResultExportFormat.Duplicati:
                    return new DuplicatiFormatSerializer();
                case ResultExportFormat.Json:
                    return new JsonFormatSerializer();
                default:
                    throw new Interface.UserInformationException(LC.L("The format is not supported: {0}", format), "SerializationFormatNotSupported");
            }
        }
    }
}
