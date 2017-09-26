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
        /// Gets information about the quota on this backend.
        /// This may return null if the particular host implementation
        /// does not support quotas, but the backend does.
        /// </summary>
        IQuotaInfo Quota { get; }
    }
}
