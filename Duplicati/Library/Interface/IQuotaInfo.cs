using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    public interface IQuotaInfo
    {
        /// <summary>
        /// The total number of bytes available on the backend,
        /// This may return -1 if the particular host implementation
        /// does not support quotas, but the backend does
        /// </summary>
        long TotalQuotaSpace { get; }
        /// <summary>
        /// The total number of unused bytes on the backend,
        /// This may return -1 if the particular host implementation
        /// does not support quotas, but the backend does
        /// </summary>
        long FreeQuotaSpace { get; }
    }
}
