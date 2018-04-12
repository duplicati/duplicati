using System;

namespace Duplicati.Library.Modules.Builtin.ResultSerialization
{
    public static class ResultFormatSerializerProvider
    {
        public static IResultFormatSerializer GetSerializer(ResultExportFormat format) {
            switch (format)
            {
                case ResultExportFormat.Duplicati:
                    {
                        return new DuplicatiFormatSerializer();
                    }
                case ResultExportFormat.Json:
                    {
                        return new JsonFormatSerializer();
                    }
                default:
                    {
                        throw new NotImplementedException(string.Format("Cannot export to {0} format yet", format));
                    }
            }
        }
    }
}
