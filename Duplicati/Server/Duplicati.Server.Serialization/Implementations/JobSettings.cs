using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization.Implementations
{
    internal class JobSettings : IJobSettings
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public IList<string> Labels { get; set; }
        public IList<string> SourcePaths { get; set; }
        public IList<IFilterSet> FilterSets { get; set; }
        public bool IncludeSetup { get; set; }
        public string BackendModule { get; set; }
        public string CompressionModule { get; set; }
        public string EncryptionModule { get; set; }
        public IDictionary<string, string> Settings { get; set; }
        public IDictionary<string, string> Overrides { get; set; }
        public IDictionary<string, string> SchedulerSettings { get; set; }
        public IBackendSettings BackendSettings { get; set; }
        public IDictionary<string, string> CompressionSettings { get; set; }
        public IDictionary<string, string> EncryptionSettings { get; set; }
    }
}
