using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Converters;

namespace Duplicati.Server.Serialization
{
    public class SerializableStatusCreator : CustomCreationConverter<Interface.IServerStatus>
    {
        public override Interface.IServerStatus Create(Type objectType)
        {
            return new Implementations.ServerStatus();
        }
    }

    public class ProgressEventDataCreator : CustomCreationConverter<Interface.IProgressEventData>
    {
        public override Interface.IProgressEventData Create(Type objectType)
        {
            return new Implementations.ProgressEventData();
        }
    }
}
