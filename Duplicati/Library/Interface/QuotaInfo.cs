using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    public class QuotaInfo : IQuotaInfo
    {
        public QuotaInfo(long totalSpace, long freeSpace)
        {
            this.TotalQuotaSpace = totalSpace;
            this.FreeQuotaSpace = freeSpace;
        }

        public long TotalQuotaSpace { get; private set; }

        public long FreeQuotaSpace { get; private set; }
    }
}
