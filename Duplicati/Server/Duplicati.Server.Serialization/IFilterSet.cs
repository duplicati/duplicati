using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization
{
    public enum FilterType
    {
        Include,
        Exclude,
        IncludeRegexp,
        ExcludeRegexp,
        ExcludeSize,
        ExcludeOlderThan,
        ExcludeNewerThan
    }

    /// <summary>
    /// Interface for a single filter set
    /// </summary>
    public interface IFilterSet
    {
        /// <summary>
        /// Name of the filterset
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// List of filters in this set
        /// </summary>
        IList<KeyValuePair<FilterType, string>> Filters { get; set; }
    }
}
