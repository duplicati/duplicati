using System;
using System.Collections.Generic;

namespace LocalizationTool2
{
	public class LocalizationEntry
	{
		public LocalizationEntry()
		{
			SourceLocations = new List<string>();
		}

		public LocalizationEntry(string text, string file, int position)
			: this()
		{
			SourceString = text;
			AddSource(file, position);
		}

		internal void AddSource(string file, int linepos)
		{
			SourceLocations.Add(string.Format("{0}:{1}", file, linepos));
		}

		public string SourceString { get; set; }
		public List<string> SourceLocations { get; set; }

}
}
