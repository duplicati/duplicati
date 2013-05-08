using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Volumes
{
    [Serializable]
    public class InvalidManifestException : Exception
    {
        public InvalidManifestException(string fieldname, string value, string expected)
            : base(string.Format("Invalid manifest detected, the field {0} has value {1} but the value {2} was expected", fieldname, value, expected))
        {
        }

        public InvalidManifestException(string message)
            : base(message)
        {
        }
    }
}
