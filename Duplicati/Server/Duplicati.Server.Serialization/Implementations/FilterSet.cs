using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization.Implementations
{
    internal class FilterSet : IFilterSet
    {
        public string Name { get; set; }
        public IList<KeyValuePair<FilterType, string>> Filters { get; set; }
    }
}
