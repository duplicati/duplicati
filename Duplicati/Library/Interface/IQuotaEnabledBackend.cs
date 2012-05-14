using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Backends that support reporting quota should implement this interface
    /// </summary>
    public interface IQuotaEnabledBackend : IBackend
    {
        /// <summary>
        /// The total number of bytes available on the backend,
        /// may return -1 if the particular host implementation
        /// does not support quotas, but the backend does
        /// </summary>
        long TotalQuotaSpace { get; }
        /// <summary>
        /// The total number of unused bytes on the backend,
        /// may return -1 if the particular host implementation
        /// does not support quotas, but the backend does
        /// </summary>
        long FreeQuotaSpace { get; }
    }
}
