using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Converters;

namespace Duplicati.Server.Serialization
{
    public class SerializableStatusCreator : CustomCreationConverter<IServerStatus>
    {
        public override IServerStatus Create(Type objectType)
        {
            return new Implementations.ServerStatus();
        }
    }

    public class ProgressEventDataCreator : CustomCreationConverter<IProgressEventData>
    {
        public override IProgressEventData Create(Type objectType)
        {
            return new Implementations.ProgressEventData();
        }
    }

    public class BackendSettingsCreator : CustomCreationConverter<IBackendSettings>
    {
        public override IBackendSettings Create(Type objectType)
        {
            return new Implementations.BackendSettings();
        }
    }

    public class FilterSetCreator : CustomCreationConverter<IFilterSet>
    {
        public override IFilterSet Create(Type objectType)
        {
            return new Implementations.FilterSet();
        }
    }
}
