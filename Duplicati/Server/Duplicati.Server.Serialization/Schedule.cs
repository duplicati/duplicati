using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.GUI.Serialization
{
    public class Schedule
    {
        public long ID { get; private set; }
        public string Name { get; set; }

        public string Path { get; set; }
        public DateTime When { get; set; }
        public string Repeat { get; set; }
        public DateTime NextScheduledTime { get; set; }
        public DayOfWeek[] AllowedWeekdays { get; set; }
    }
}
