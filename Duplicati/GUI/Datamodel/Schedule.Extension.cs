using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Datamodel
{
    public partial class Schedule
    {
        public bool ExistsInDb
        {
            get { return this.ID > 0; }
        }
    }
}
