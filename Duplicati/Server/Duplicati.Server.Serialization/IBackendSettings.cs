using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization
{
    public interface IBackendSettings
    {
        /// <summary>
        /// Gets or sets a string with all relevant properties encoded as a semi-valid URI
        /// </summary>
        string ConnectionURI { get; set; }

        /// <summary>
        /// The username used to connect to the server
        /// </summary>
        string Username { get; set; }

        /// <summary>
        /// The password used to connect to the server
        /// </summary>
        string Password { get; set; }

        /// <summary>
        /// A lookup table with settings applied to the backend
        /// </summary>
        IDictionary<string, string> Settings { get; set; }
    }
}
