using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Main.LiveControl
{
    class ExecutionStoppedException : Exception
    {
        public ExecutionStoppedException()
            : base(Strings.ExecutionStoppedException.DefaultMessage)
        {
        }
    }
}
