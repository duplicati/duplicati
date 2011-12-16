using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Converters;

namespace Duplicati.Server.Serialization
{
    public class SerializableStatusCreator : CustomCreationConverter<ISerializableStatus>
    {
        public override ISerializableStatus Create(Type objectType)
        {
            return new Implementations.SerializableStatus();
        }
    }

    public class ProgressEventDataCreator : CustomCreationConverter<IProgressEventData>
    {
        public override IProgressEventData Create(Type objectType)
        {
            return new Implementations.ProgressEventData();
        }
    }
}
