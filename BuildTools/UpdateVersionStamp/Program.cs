using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UpdateVersionStamp
{
    public static class Program
    {
        private static readonly string DIR_SEP = Path.DirectorySeparatorChar.ToString();
        private static readonly Dictionary<string, Regex> FILEMAP;
        
        static Program()
        {
            var versionre = @"(?<version>\d+\.\d+\.(\*|(\d+(\.(\*|\d+)))?))";
            FILEMAP = new Dictionary<string, Regex>(StringComparer.InvariantCultureIgnoreCase);
            FILEMAP.Add("AssemblyInfo.cs", new Regex(@"(\[assembly\: AssemblyVersion\(\""" + versionre + @"""\)\])|(\[assembly\: AssemblyFileVersion\(\""" + versionre + @"\""\)\])|(\[assembly\: AssemblyFileVersionAttribute\(\""" + versionre + @"\""\)\])"));
            FILEMAP.Add("UpgradeData.wxi", new Regex(@"\<\?define ProductVersion\=\""" + versionre + @"\"" \?\>"));
            FILEMAP.Add("AssemblyRedirects.xml", new Regex(@"newVersion\=\""" + versionre + @"\"""));
        }
        
        private class Options
        {
            public string sourcefolder = Path.GetFullPath(Environment.CurrentDirectory);
            public string ignorefilter = null;
            public string version = null;
            public string versiontag = null;
            
            public void Fixup()
            {
                sourcefolder = Duplicati.Library.Common.IO.Util.AppendDirSeparator(System.IO.Path.GetFullPath(sourcefolder.Replace("/", DIR_SEP)));
                if (ignorefilter != null)
                    ignorefilter =ignorefilter.Replace("/", DIR_SEP);
            }
        }        
        
        public static void Main(string[] _args)
        {
            List<string> args = new List<string>(_args);
            Dictionary<string, string> options = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(args);
            Options opt = new Options();
            
            foreach (FieldInfo fi in opt.GetType().GetFields())
                if (options.ContainsKey(fi.Name))
                    fi.SetValue(opt, options[fi.Name]);
            
            opt.Fixup();            
            
            Duplicati.Library.Utility.IFilter filter = null;
            if (!string.IsNullOrEmpty(opt.ignorefilter))
                filter = new Duplicati.Library.Utility.FilterExpression(opt.ignorefilter, false);
            
            Func<string, bool> isFile = (string x) => !x.EndsWith(DIR_SEP);
            
            var paths = Duplicati.Library.Utility.Utility.EnumerateFileSystemEntries(opt.sourcefolder, filter)
                .Where(x => isFile(x) && FILEMAP.ContainsKey(Path.GetFileName(x)))
                .Select(x =>
            {
                var m = FILEMAP[Path.GetFileName(x)].Match(File.ReadAllText(x));
                return m.Success ? 
                        new { File = x, Version = new Version(m.Groups["version"].Value.Replace("*", "0")), Display = m.Groups["version"].Value } 
                        : null;
            })
                .Where(x => x != null)
                .ToArray(); //No need to re-eval
            
            if (paths.Count() == 0)
            {
                Console.WriteLine("No files found to update...");
                return;
            }
            
            foreach (var p in paths)
                Console.WriteLine("{0}\t:{1}", p.Display, p.File);
            
            if (string.IsNullOrWhiteSpace(opt.version))
            {
                var maxv = paths.Select(x => x.Version).Max();
                opt.version = new Version(
                    maxv.Major,
                    maxv.Minor,
                    maxv.Build,
                    maxv.Revision).ToString();
            }
            
            //Sanity check
            var nv = new Version(opt.version).ToString(4);

            foreach (var p in paths)
            {
                var re = FILEMAP[Path.GetFileName(p.File)];
                var txt = File.ReadAllText(p.File);
                //var m = re.Match(txt).Groups["version"];
                txt = re.Replace(txt, (m) => {
                    var t = m.Groups["version"];
                    return m.Value.Replace(t.Value, nv);
                });
                File.WriteAllText(p.File, txt);
            }

            Console.WriteLine("Updated {0} files to version {1}", paths.Count(), opt.version);
        }
    }
}
