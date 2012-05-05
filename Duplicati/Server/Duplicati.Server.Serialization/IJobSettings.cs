using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization
{
    /// <summary>
    /// Interface describing a backup job
    /// </summary>
    public interface IJobSettings
    {
        /// <summary>
        /// The internal database job ID
        /// </summary>
        long Id { get; set; }
        /// <summary>
        /// The name supplied by the user that identifies the backup
        /// </summary>
        string Name { get; set; }
        /// <summary>
        /// A list of labels associated with the backup job
        /// </summary>
        IList<string> Labels { get; set; }
        /// <summary>
        /// A list manually set source paths
        /// </summary>
        IList<string> SourcePaths { get; set; }
        /// <summary>
        /// The filter sets applied to the source of this backup job
        /// </summary>
        IList<IFilterSet> FilterSets { get; set; }
        /// <summary>
        /// A value indicating if the setup database is included in the backup
        /// </summary>
        bool IncludeSetup { get; set; }
        /// <summary>
        /// The backend module key
        /// </summary>
        string BackendModule { get; set; }
        /// <summary>
        /// The compression module key
        /// </summary>
        string CompressionModule { get; set; }
        /// <summary>
        /// The encryption module key
        /// </summary>
        string EncryptionModule { get; set; }

        /// <summary>
        /// All general settings for a backup job
        /// </summary>
        IDictionary<string, string> Settings { get; set; }
        /// <summary>
        /// All settings explicitly overridden by the user
        /// </summary>
        IDictionary<string, string> Overrides { get; set; }

        /// <summary>
        /// All settings relating to the scheduler
        /// </summary>
        IDictionary<string, string> SchedulerSettings { get; set; }
        /// <summary>
        /// All settings relating to the backend module
        /// </summary>
        IBackendSettings BackendSettings { get; set; }
        /// <summary>
        /// All settings relating to the compression module
        /// </summary>
        IDictionary<string, string> CompressionSettings { get; set; }
        /// <summary>
        /// All settings relating to the encryption module
        /// </summary>
        IDictionary<string, string> EncryptionSettings { get; set; }

    }
}
