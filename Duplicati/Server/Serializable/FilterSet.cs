using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serializable
{
    public class FilterSet : Serialization.IFilterSet
    {
        public FilterSet()
        {
            this.Filters = new List<KeyValuePair<Serialization.FilterType, string>>();
        }

        public string Name { get; set; }
        public IList<KeyValuePair<Serialization.FilterType, string>> Filters { get; set; }
    }
}
