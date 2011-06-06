using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.GUI.Serialization
{
	public class Task
	{
		public long ID { get; private set; }
		public string Service { get; set; }
		public string Encryptionkey { get; set; }
		public string SourcePath { get; set; }
		public long ScheduleID { get; set; }
		public long KeepFull { get; set; }
		public string KeepTime { get; set; }
		public string FullAfter { get; set; }
		public bool IncludeSetup { get; set; }
		public string EncryptionModule { get; set; }
		public string CompressionModule { get; set; }
	}
}
