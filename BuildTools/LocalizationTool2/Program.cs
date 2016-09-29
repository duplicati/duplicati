using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LocalizationTool2
{
	class MainClass
	{
		public static int Main(string[] args)
		{
			var sourcefolder = Environment.CurrentDirectory;
			if (args == null || args.Length != 0)
				sourcefolder = args[0];

			sourcefolder = Path.GetFullPath(sourcefolder.Replace("~", Environment.GetEnvironmentVariable("HOME")));

            var targetfolder = sourcefolder;
            if (args == null || args.Length > 1)
                targetfolder = Path.GetFullPath(args[1].Replace("~", Environment.GetEnvironmentVariable("HOME")));


			if (!Directory.Exists(sourcefolder))
			{
				Console.WriteLine("No such directory: {0}", sourcefolder);
				return 1;
			}

			sourcefolder = Path.GetFullPath(sourcefolder);
			Console.WriteLine("Using directory {0}, scanning ....", sourcefolder);


			var searchlist = new Dictionary<string, Regex>();
			searchlist.Add("html", new Regex("((\\{\\{)|(ng-bind-html\\s*=\\s*\"))\\s*\\'(?<sourcestring>(\\\\\\'|[^\\'])+)\\'\\s*\\|\\s*localize(\\s|\\:|\\})", RegexOptions.Multiline | RegexOptions.IgnoreCase));
			searchlist.Add("js", new Regex("Localization\\.localize\\(\\s*((\\'(?<sourcestring>(\\\\\\'|[^\\'])+)\\')|(\\\"(?<sourcestring>(\\\\\\\"|[^\\\"])+)\\\"))", RegexOptions.Multiline | RegexOptions.IgnoreCase));
            searchlist.Add("cs", new Regex("LC.L\\s*\\(((@\\s*\"(?<sourcestring>(\"\"|[^\"])+))|(\"(?<sourcestring>(\\\\\"|[^\"])+)))\"\\s*(\\)|,)", RegexOptions.Multiline | RegexOptions.IgnoreCase));

			var map = new Dictionary<string, LocalizationEntry>();

            if (!sourcefolder.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                sourcefolder += Path.DirectorySeparatorChar;

			foreach (var ext in searchlist.Keys)
			{
				var re = searchlist[ext];
				foreach (var f in Directory.GetFiles(sourcefolder, "*." + ext, SearchOption.AllDirectories))
				{
					var txt = File.ReadAllText(f);
					foreach (Match match in re.Matches(txt))
					{
						var linepos = txt.Substring(match.Index).Count(x => x == '\n');
                        var str = match.Groups["sourcestring"].Value.Replace("\n", "\\n").Replace("\r", "\\r");
						LocalizationEntry le;
						if (!map.TryGetValue(str, out le))
                            map[str] = new LocalizationEntry(str, f.Substring(sourcefolder.Length), linepos);
						else
							le.AddSource(Path.GetFileName(f), linepos);						
					}
				}
			}

			//File.WriteAllText(Path.Combine(sourcefolder, "translations.json"), Newtonsoft.Json.JsonConvert.SerializeObject(map.Values.OrderBy(x => x.SourceLocations.FirstOrDefault()).ToArray(), Newtonsoft.Json.Formatting.Indented));
			//File.WriteAllText(Path.Combine(sourcefolder, "translations-list.json"), Newtonsoft.Json.JsonConvert.SerializeObject(map.Select(x => x.Key).OrderBy(x => x).ToArray(), Newtonsoft.Json.Formatting.Indented));

			File.WriteAllLines(
				Path.Combine(targetfolder, "localization.po"),
                map.OrderBy(x => x.Key).Select(x => x.Value).SelectMany(x => new string[] { 
					"#: " + x.SourceLocations.FirstOrDefault(),
                    string.Format("msgid \"{0}\"", (x.SourceString ?? "")),
                    string.Format("msgstr \"{0}\"", (x.SourceString ?? "")),
                    ""
				}),
				System.Text.Encoding.UTF8
			);

            Console.WriteLine("Wrote {0} strings to {1}", map.Count, Path.Combine(targetfolder, "localization.po"));

            /*File.WriteAllLines(
                Path.Combine(sourcefolder, "localization-by-file.po"),
                map.OrderBy(x => x.Value.SourceLocations.FirstOrDefault()).Select(x => x.Value).SelectMany(x => new string[] {
                    "#: " + x.SourceLocations.FirstOrDefault(),
                    string.Format("msgid \"{0}\"", (x.SourceString ?? "")),
                    string.Format("msgstr \"{0}\"", (x.SourceString ?? "")),
                    ""
                }),
                System.Text.Encoding.UTF8
            );*/

			return 0;

		}
	}
}
