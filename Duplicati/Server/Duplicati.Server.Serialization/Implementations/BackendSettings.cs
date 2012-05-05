using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization.Implementations
{
    public class BackendSettings : IBackendSettings
    {
        public string ConnectionURI { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public IDictionary<string, string> Settings { get; set; }
    }
}
