using Duplicati.Library.Localization.Short;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Strings
{
    internal static class Tardigrade
    {
        public static string DisplayName { get { return LC.L(@"Tardigrade Decentralised Cloud Storage"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to the Tardigrade Decentralized Cloud Storage. It is deprecated - please move over to the new Storj DCS."); } }
    }
}
