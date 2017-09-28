using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Contains extension methods for the IBackend interfaces
    /// </summary>
    public static class BackendExtensions
    {
        /// <summary>
        /// Tests a backend by invoking the List() method.
        /// As long as the iteration can either complete or find at least one file without throwing, the test is successful
        /// </summary>
        /// <param name="backend">Backend to test</param>
        public static void TestList(this IBackend backend)
        {
            // If we can iterate successfully, even if it's empty, then the backend test is successful
            foreach (IFileEntry file in backend.List())
            {
                break;
            }
        }
    }
}
