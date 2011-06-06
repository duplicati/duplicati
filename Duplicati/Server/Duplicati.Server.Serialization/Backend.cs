using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.GUI.Serialization
{
    public class Backend
    {
        public string ProtocolKey { get; private set; }
        public string DisplayName { get; private set; }
        public string Description { get; private set; }

        public Duplicati.Library.Interface.ICommandLineArgument[] SupportedCommands { get; private set; }
    }
}
