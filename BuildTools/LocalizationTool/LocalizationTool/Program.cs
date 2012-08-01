#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.IO;
using System.Net;

namespace LocalizationTool
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            //All relative paths are to the app dir
            System.IO.Directory.SetCurrentDirectory(Application.StartupPath);

            string loc = args.Length >= 2 ? args[1] : null;

            switch (args[0].ToLower())
            {
                case "clean":
                case "cleanup":
                    Clean(loc);
                    break;
                case "build":
                    Compile(loc);
                    break;
                case "update":
                    Update(loc);
                    break;
                case "create":
                    if (args.Length != 2)
                    {
                        Console.WriteLine("missing locale identifier");
                        PrintUsage();
                        return;
                    }
                    Create(loc);
                    break;
                case "report":
                    Report(loc);
                    break;
                case "export":
                    Export(loc);
                    break;
                case "exportdiff":
                    if (args.Length != 3)
                    {
                        Console.WriteLine("missing locale identifier or input CSV file");
                        PrintUsage();
                        return;
                    }

                    ExportDiff(loc, args[2]);
                    break;
                case "import":
                    if (args.Length != 3)
                    {
                        Console.WriteLine("missing locale identifier or input CSV file");
                        PrintUsage();
                        return;
                    }
                    Import(loc, args[2]);
                    break;
                case "guiupdate":
                    Application.EnableVisualStyles();
                    Application.DoEvents();

                    Report(loc);
                    new UpdateGUI(System.IO.Path.Combine(Application.StartupPath, "report." + loc + ".xml")).ShowDialog();
                    break;
                case "webpull":
                    WebPull(loc, false);
                    break;
                case "webupdate":
                    WebPull(loc, true);
                    break;
                default:
                    PrintUsage();
                    return;
            }
        }


        private static void PrintUsage()
        {
            Console.WriteLine("Usage: ");

            Console.WriteLine("LocalizationTool.exe CLEAN [locale identifier]");
            Console.WriteLine("LocalizationTool.exe BUILD [locale identifier]");
            Console.WriteLine("LocalizationTool.exe UPDATE [locale identifier]");
            Console.WriteLine("LocalizationTool.exe REPORT [locale identifier]");
            Console.WriteLine("LocalizationTool.exe CREATE <locale indentifier>");
            Console.WriteLine("LocalizationTool.exe GUIUPDATE <locale identifier>");
            Console.WriteLine("LocalizationTool.exe EXPORT [locale identifier]");
            Console.WriteLine("LocalizationTool.exe IMPORT <locale identifier> <input CSV file>");
            Console.WriteLine("LocalizationTool.exe EXPORTDIFF <locale identifier> <input CSV file>");
            Console.WriteLine("LocalizationTool.exe WEBPULL [locale identifier]");
            Console.WriteLine("LocalizationTool.exe WEBUPDATE [locale identifier]");
        }

        private struct CSVEntry
        {
            public string Filename;
            public string Fieldkey;
            public string Origvalue;
            public string Value;
            public string Status;

            public string[] extraFields;

            public CSVEntry(List<string> fields)
            {
                Filename = fields[0];
                Fieldkey = fields[1];
                Status = fields[2];
                Origvalue = fields[3];
                Value = fields[4];

                if (fields.Count > 5)
                {
                    extraFields = new string[fields.Count - 5];
                    fields.CopyTo(5, extraFields, 0, extraFields.Length);
                }
                else
                    extraFields = null;
            }
        }

        private static bool IsValidCulture(string c)
        {
            try
            {
                System.Globalization.CultureInfo.GetCultureInfo(c);
                return true;
            }
            catch
            {
                return false;
            }
        }        

        /// <summary>
        /// Downloads updated CSV files from the web and generates diff files, and optionally updates the local CSV files
        /// </summary>
        /// <param name="loc">The culture to use</param>
        /// <param name="update">True if the elements should be updated as well</param>
        private static void WebPull(string loc, bool update)
        {
            var locs =
                from n in Directory.GetFiles(Application.StartupPath, "report.*.url")
                where IsValidCulture(Path.GetFileNameWithoutExtension(n).Substring("report.".Length))
                select Path.GetFileNameWithoutExtension(n).Substring("report.".Length);

            if (!string.IsNullOrEmpty(loc))
            {
                locs = locs.Where(c => string.Equals(c, loc, StringComparison.CurrentCultureIgnoreCase));
                if (locs.Count() == 0)
                {
                    Console.WriteLine("No URL file found for culture {0}", loc);
                    return;
                }
            }

            if (locs.Count() == 0)
            {
                Console.WriteLine("No culture.url files found");
                return;
            }

            foreach (var culture in locs)
            {
                var url = string.Format("report.{0}.url", culture);
                var file = string.Format("report.{0}.new.csv", culture);

                if (File.Exists(file))
                    File.Delete(file);
                Console.Write("Downloading updated CSV for {0} ... ", culture);
                try { new WebClient().DownloadFile(File.ReadAllText(url).Trim(), file); }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed!");
                    Console.WriteLine(" -> {0}", ex.Message);
                    continue;
                }
                Console.WriteLine("Done!");

                if (update)
                {
                    Console.Write("Updating local files for {0} .", culture);
                    ExportDiff(culture, file);
                    Console.Write(".");
                    Import(culture, file);
                    Console.Write(".");
                    Export(culture);
                    Console.WriteLine(" Done!");
                }

            }

        }

        /// <summary>
        /// Imports a CSV file into a dictionary format,
        /// Outer key is filename, inner key is fieldname, inner value is translated text
        /// </summary>
        /// <param name="file">The CSV file to read from</param>
        /// <param name="loc">The current culture string</param>
        /// <returns>Dictionary where outer key is filename, inner key is fieldname, inner value is translated text</returns>
        private static Dictionary<string, Dictionary<string, CSVEntry>> ImportCSV(string file, string loc, Func<CSVEntry, bool> filter)
        {
            Dictionary<string, Dictionary<string, CSVEntry>> values = new Dictionary<string, Dictionary<string, CSVEntry>>();

            using (CSVReader r = new CSVReader(file))
            {
                List<string> fields = null;
                while ((fields = r.AdvanceLine()) != null)
                    if (fields.Count >= 5)
                    {
                        CSVEntry e = new CSVEntry(fields);
                        e.Filename = System.IO.Path.Combine(System.IO.Path.Combine(Application.StartupPath, loc), e.Filename);

                        if (filter != null && filter(e))
                            continue;

                        if (!values.ContainsKey(e.Filename))
                            values.Add(e.Filename, new Dictionary<string, CSVEntry>());

                        Dictionary<string, CSVEntry> l = values[e.Filename];
                        l[e.Fieldkey] = e;
                    }
            }

            return values;
        }

        private static void Import(string loc, string p)
        {
            //Outer key is filename, inner key is fieldname, inner value is translated text
            Dictionary<string, Dictionary<string, CSVEntry>> values = ImportCSV(p, loc, e => e.Value.Trim().Length == 0 || e.Value == e.Origvalue);
			
			if (Duplicati.Library.Utility.Utility.IsClientLinux)
			{
				Dictionary<string, Dictionary<string, CSVEntry>> remap = new Dictionary<string, Dictionary<string, CSVEntry>>();
				foreach(string key in values.Keys)
					remap[key.Replace("\\", System.IO.Path.DirectorySeparatorChar.ToString ())] = values[key];
				
				values = remap;
			}
			
            string folder = System.IO.Path.Combine(Application.StartupPath, loc);
            if (!System.IO.Directory.Exists(folder))
                Create(loc);
            else
                Update(loc);

            XNamespace xmlns = "";
            foreach (ResXFileInfo inf in GetResXList(loc))
            {
                if (System.IO.File.Exists(inf.TargetFile) && values.ContainsKey(inf.TargetFile))
                {
                    XDocument targetDoc = XDocument.Load(inf.TargetFile);
                    XNode insertTarget = targetDoc.Element("root").LastNode;
                    var targetVals = targetDoc.Element("root").Elements("data").ToSafeDictionary(c => c.Attribute("name").Value, inf.TargetFile);
                    var sourceVals = values[inf.TargetFile];
                    values.Remove(inf.TargetFile);

                    bool updated = false;
                    foreach (var item in sourceVals)
                        if (targetVals.ContainsKey(item.Key))
                        {
                            if (targetVals[item.Key].Element("value").Value.Replace("\r\n", "\n") != item.Value.Value.Replace("\r\n", "\n"))
                            {
                                updated = true;
                                targetVals[item.Key].Element("value").Value = item.Value.Value;
                            }
                        }
                        else
                        {
                            updated = true;
                            insertTarget.AddAfterSelf(new XElement("data",
                                new XAttribute("name", item.Key),
                                new XAttribute(xmlns + "space", "preserve"),
                                new XElement("value", item.Value.Value)
                            ));
                        }

                    if (updated)
                        targetDoc.Save(inf.TargetFile);
                }
            }

            if (values.Count != 0)
                Console.WriteLine("The following files were translated but did not exist: " + string.Join(Environment.NewLine, values.Keys.ToArray()));
        }


        public static IEnumerable<string> GetLocaleFolders(string locid)
        {
            if (string.IsNullOrEmpty(locid))
                return System.IO.Directory.GetDirectories(Application.StartupPath).Where(c =>
                {
                    try
                    {
                        System.Globalization.CultureInfo.GetCultureInfo(System.IO.Path.GetFileName(c));
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }).Select(c => System.IO.Path.GetFileName(c));
            else
                return new string[] { locid };
        }

        private static void Export(string cultures)
        {
            Update(cultures);
            Report(cultures);

            foreach (string culture in GetLocaleFolders(cultures))
            {
                XDocument doc = XDocument.Load(System.IO.Path.Combine(Application.StartupPath, "report." + culture + ".xml"));
                string outfile = System.IO.Path.Combine(Application.StartupPath, "report." + culture + ".csv");

                Dictionary<string, Dictionary<string, CSVEntry>> elems = new Dictionary<string, Dictionary<string, CSVEntry>>();
                foreach (var file in doc.Element("root").Elements("file"))
                {
                    string filename = file.Attribute("filename").Value.Substring(Duplicati.Library.Utility.Utility.AppendDirSeparator(Application.StartupPath).Length);
                    filename = filename.Substring(culture.Length + 1);

                    Dictionary<string, CSVEntry> e = new Dictionary<string, CSVEntry>();
                    elems[filename] = e;
					
                    foreach (var item in file.Element("updated").Elements("item"))
                    {
                        CSVEntry c = new CSVEntry();
                        c.Filename = filename;
                        c.Fieldkey = item.Attribute("name").Value;
                        c.Status = item.Parent.Name.LocalName;
                        c.Origvalue = item.Element("original").Value;
                        c.Value = item.Element("translated").Value;
                        e.Add(c.Fieldkey, c);

                    }

                    foreach (var item in file.Element("missing").Elements("item"))
                    {
                        CSVEntry c = new CSVEntry();
                        c.Filename = filename;
                        c.Fieldkey = item.Attribute("name").Value;
                        c.Status = item.Parent.Name.LocalName;
                        c.Origvalue = item.Value;
                        c.Value = "";
                        e.Add(c.Fieldkey, c);
                    }

                    foreach (var item in file.Element("not-updated").Elements("item"))
                    {
                        CSVEntry c = new CSVEntry();
                        c.Filename = filename;
                        c.Fieldkey = item.Attribute("name").Value;
                        c.Status = item.Parent.Name.LocalName;
                        c.Origvalue = item.Value;
                        c.Value = "";
                        e.Add(c.Fieldkey, c);
                    }

                    foreach (var item in file.Element("unused").Elements("item"))
                    {
                        CSVEntry c = new CSVEntry();
                        c.Filename = filename;
                        c.Fieldkey = item.Attribute("name").Value;
                        c.Status = item.Parent.Name.LocalName;
                        c.Origvalue = "";
                        c.Value = item.Value;
                        e.Add(c.Fieldkey, c);
                    }

                }

                WriteCSVFile(outfile, culture, elems, null);
            }
        }

        private static void ExportDiff(string culture, string inputfile)
        {
            inputfile = System.IO.Path.GetFullPath(inputfile);

            string currentFile = System.IO.Path.Combine(Application.StartupPath, "report." + culture + ".csv");
            string diffFile = System.IO.Path.Combine(Application.StartupPath, "report." + culture + ".diff.csv");

            if (currentFile.Equals(inputfile, Duplicati.Library.Utility.Utility.ClientFilenameStringComparision))
                throw new Exception("Input file will be overwritten, please use another file");

            Export(culture);


            //Outer key is filename, inner key is fieldname, inner value is translated text
            Dictionary<string, Dictionary<string, CSVEntry>> inputValues = ImportCSV(inputfile, culture, null);
            Dictionary<string, Dictionary<string, CSVEntry>> currentValues = ImportCSV(currentFile, culture, null);

            Dictionary<string, Dictionary<string, CSVEntry>> added = new Dictionary<string, Dictionary<string, CSVEntry>>();
            Dictionary<string, Dictionary<string, CSVEntry>> removed = new Dictionary<string, Dictionary<string, CSVEntry>>();

            foreach (var f in currentValues)
            {
                Dictionary<string, CSVEntry> other;
                inputValues.TryGetValue(f.Key, out other);

                if (other == null)
                {
                    added.Add(f.Key, f.Value);
                }
                else
                {
                    Dictionary<string, CSVEntry> a = new Dictionary<string,CSVEntry>();

                    foreach (KeyValuePair<string, CSVEntry> s in f.Value)
                    {
                        if (other.ContainsKey(s.Key))
                            other.Remove(s.Key);
                        else
                            a.Add(s.Key, s.Value);
                    }

                    if (a.Count > 0)
                        added.Add(f.Key, a);

                    if (other.Count > 0)
                        removed.Add(f.Key, other);
                }
            }

            Dictionary<string, Dictionary<string, string>> overrides = new Dictionary<string, Dictionary<string, string>>();
            Dictionary<string, Dictionary<string, CSVEntry>> diffElems = new Dictionary<string, Dictionary<string, CSVEntry>>();

            foreach (KeyValuePair<string, Dictionary<string, CSVEntry>> f in added)
                foreach (KeyValuePair<string, CSVEntry> s in f.Value)
                    if (s.Value.Origvalue == s.Value.Value || s.Value.Value.Trim().Length == 0)
                    {
                        if (!overrides.ContainsKey(f.Key))
                            overrides.Add(f.Key, new Dictionary<string, string>());
                        if (!diffElems.ContainsKey(f.Key))
                            diffElems.Add(f.Key, new Dictionary<string, CSVEntry>());
                        overrides[f.Key].Add(s.Key, "not-updated");
                        diffElems[f.Key].Add(s.Key, s.Value);
                    }

            foreach (KeyValuePair<string, Dictionary<string, CSVEntry>> f in removed)
                foreach (KeyValuePair<string, CSVEntry> s in f.Value)
                {
                    if (!overrides.ContainsKey(f.Key))
                        overrides.Add(f.Key, new Dictionary<string, string>());
                    if (!diffElems.ContainsKey(f.Key))
                        diffElems.Add(f.Key, new Dictionary<string, CSVEntry>());
                    overrides[f.Key].Add(s.Key, "unused");
                    diffElems[f.Key].Add(s.Key, s.Value);
                }

            WriteCSVFile(diffFile, culture, diffElems, overrides);

            //Re-read the file
            inputValues = ImportCSV(inputfile, culture, null);

            //Add the new entries
            foreach (KeyValuePair<string, Dictionary<string, CSVEntry>> f in added)
            {
                if (!inputValues.ContainsKey(f.Key))
                    inputValues.Add(f.Key, new Dictionary<string, CSVEntry>());

                Dictionary<string, CSVEntry> o = inputValues[f.Key];

                foreach (KeyValuePair<string, CSVEntry> s in f.Value)
                    o.Add(s.Key, s.Value);

            }

            foreach (KeyValuePair<string, Dictionary<string, CSVEntry>> f in currentValues)
            {
                Dictionary<string, CSVEntry> o = inputValues[f.Key];
                foreach (KeyValuePair<string, CSVEntry> s in f.Value)
                {
                    CSVEntry c = o[s.Key];
                    if (s.Value.Status == "unused")
                        c.Status = "unused";
                    else if (c.Origvalue == c.Value)
                        c.Status = "not-updated";
                    else if (string.IsNullOrEmpty(c.Value) || c.Value.Trim().Length == 0)
                        c.Status = "missing";
                    else
                        c.Status = "updated";

                    o[s.Key] = c;
                }
            }

            //Update the removed entries
            foreach (KeyValuePair<string, Dictionary<string, CSVEntry>> f in removed)
            {
                Dictionary<string, CSVEntry> o = inputValues[f.Key];
                foreach (KeyValuePair<string, CSVEntry> s in f.Value)
                {
                    CSVEntry c = o[s.Key];
                    c.Status = "unused";
                    o[s.Key] = c;
                }
            }



            //Write the output file
            diffFile = System.IO.Path.Combine(Application.StartupPath, "report." + culture + ".updated.csv");
            WriteCSVFile(diffFile, culture, inputValues, null);
        }

        private static void WriteCSVFile(string filename, string culture, Dictionary<string, Dictionary<string, CSVEntry>> entries, Dictionary<string, Dictionary<string, string>> status_overrides)
        {
            if (status_overrides == null)
                status_overrides = new Dictionary<string, Dictionary<string, string>>();

            List<KeyValuePair<string, KeyValuePair<string, CSVEntry>>> sortlist = new List<KeyValuePair<string, KeyValuePair<string, CSVEntry>>>();
            foreach (KeyValuePair<string, Dictionary<string, CSVEntry>> f in entries)
                foreach (KeyValuePair<string, CSVEntry> s in f.Value)
                    sortlist.Add(new KeyValuePair<string, KeyValuePair<string, CSVEntry>>(f.Key, s));

            var sortedlist = sortlist.OrderBy(x => x.Key + System.IO.Path.PathSeparator + x.Value.Key);

            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(filename, false, new System.Text.UTF8Encoding(false)))
            {
                string filenameprefix = System.IO.Path.Combine(Application.StartupPath, culture);
                int pfl = filenameprefix.Length + 1;

                foreach (KeyValuePair<string, KeyValuePair<string, CSVEntry>> f in sortedlist)
                {
                    string status = f.Value.Value.Status;
                    if (status_overrides.ContainsKey(f.Key) && status_overrides[f.Key].ContainsKey(f.Value.Key))
                        status = status_overrides[f.Key][f.Value.Key];

                    string path = f.Key;
                    if (f.Key.StartsWith(filenameprefix, Duplicati.Library.Utility.Utility.ClientFilenameStringComparision))
                        path = f.Key.Substring(Math.Min(pfl, f.Key.Length));
					
					if (Duplicati.Library.Utility.Utility.IsClientLinux)
						path = path.Replace(System.IO.Path.DirectorySeparatorChar.ToString(), "\\");

                    WriteCSVLine(sw, path, f.Value.Key, status, f.Value.Value.Origvalue, f.Value.Value.Value, f.Value.Value.extraFields);
                }
            }
        }

        private static string CSV_SEPARATOR = ",";

        private static void WriteCSVLine(System.IO.StreamWriter sw, string filename, string key, string status, string originalText, string translatedText, string[] extraFields)
        {
            sw.Write(EscapeCSVString(filename));
            sw.Write(CSV_SEPARATOR);
            sw.Write(EscapeCSVString(key));
            sw.Write(CSV_SEPARATOR);
            sw.Write(EscapeCSVString(status));
            sw.Write(CSV_SEPARATOR);
            sw.Write(EscapeCSVString(originalText));
            sw.Write(CSV_SEPARATOR);
            sw.Write(EscapeCSVString(translatedText));

            if (extraFields != null && extraFields.Length > 0)
                foreach (string s in extraFields)
                {
                    sw.Write(CSV_SEPARATOR);
                    sw.Write(EscapeCSVString(s));
                }
			
			sw.Write("\r\n");
        }

        private static string EscapeCSVString(string value)
        {
            //.Replace("\\", "\\\\").Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n").Replace("\t", "\\t")
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static void Report(string cultures)
        {
            foreach (string culture in GetLocaleFolders(cultures))
            {
                Dictionary<string, string> ignores = new Dictionary<string, string>();
                string ignorefile = System.IO.Path.Combine(Application.StartupPath, "ignore." + culture + ".xml");
                if (System.IO.File.Exists(ignorefile))
                    ignores = XDocument.Load(ignorefile).Element("root").Elements("ignore").Select(c => c.Value).ToSafeDictionary(c => c.ToLower().Trim(), ignorefile);

                //Make sure we ignore empty strings
                ignores[""] = "";

                //Ignore common leftover values
                for (int i = 1; i < 20; i++)
                {
                    string _i = i.ToString();
                    ignores["textbox" + _i] = "";
                    ignores["combobox" + _i] = "";
                    ignores["checkbox" + _i] = "";
                    ignores["label" + _i] = "";
                    ignores["radiobutton" + _i] = "";
                    ignores["button" + _i] = "";
                    ignores["toolstrip" + _i] = "";
                    ignores["toolstripbutton" + _i] = "";
                    ignores["statusstrip" + _i] = "";
                }


                XDocument report = new XDocument(
                    new XElement("root")
                );
                XElement reportRoot = report.Element("root");

                IEnumerable<ResXFileInfo> files = GetResXList(culture);
                var missingFiles = files.Where(c => !System.IO.File.Exists(c.TargetFile));
                var existingFiles = files.Where(c => System.IO.File.Exists(c.TargetFile));

                var extraFiles = from x in Duplicati.Library.Utility.Utility.EnumerateFiles(System.IO.Path.Combine(Application.StartupPath, culture))
                                 where 
                                    x.EndsWith("." + culture + ".resx")
                                    &&
                                    !files.Select(c => c.TargetFile).Contains(x)
                                select x;

                reportRoot.Add(
                    new XElement("files",
                        new XElement("missing",
                            from x in missingFiles
                            select new XElement("file", x.TargetFile)
                        ),
                        new XElement("unused",
                            from x in extraFiles
                            select new XElement("file", x)
                        )
                   )
                );

                foreach(ResXFileInfo inf in existingFiles)
                {
                    IEnumerable<XElement> sourceElements = XDocument.Load(inf.SourceFile).Element("root").Elements("data");
                    IEnumerable<XElement> targetElements = XDocument.Load(inf.TargetFile).Element("root").Elements("data");

                    if (inf.IsForm)
                    {
                        //Look only for strings
                        Func<XElement, bool> filter = 
                            c => 
                            !c.Attribute("name").Value.StartsWith(">>")
                            &&
                            c.Attribute("mimetype") == null
                            &&
                            (
                                c.Attribute("type") == null
                                ||
                                c.Attribute("type").Value == "System.String, mscorlib"
                                ||
                                c.Attribute("type").Value == "System.String"
                            );

                        sourceElements = sourceElements.Where(filter);
                        targetElements = targetElements.Where(filter);
                    }

                    //Filter the source and target before proceeding
                    sourceElements = sourceElements.Where(c => !ignores.ContainsKey(c.Element("value").Value.Trim().ToLower()) && !c.Element("value").Value.Trim().ToLower().StartsWith("..\\resources\\") && !c.Element("value").Value.Trim().ToLower().StartsWith("../resources/"));
                    targetElements = targetElements.Where(c => !ignores.ContainsKey(c.Element("value").Value.Trim().ToLower()) && !c.Element("value").Value.Trim().ToLower().StartsWith("..\\resources\\") && !c.Element("value").Value.Trim().ToLower().StartsWith("../resources/"));

                    var sourceVals = sourceElements.ToSafeDictionary(c => c.Attribute("name").Value, inf.SourceFile);
                    var targetVals = targetElements.ToSafeDictionary(c => c.Attribute("name").Value, inf.TargetFile);

                    var missing = sourceVals.Where(c => !targetVals.ContainsKey(c.Key)).ToList();
                    var unused = targetVals.Where(c => !sourceVals.ContainsKey(c.Key)).ToList();
                    var notUpdated = sourceVals.Where(c =>
                        targetVals.ContainsKey(c.Key)
                        &&
                        targetVals[c.Key].Element("value").Value == c.Value.Element("value").Value).ToList();

                    var updated = sourceVals.Where(c =>
                        !missing.Contains(c)
                        &&
                        !unused.Contains(c)
                        &&
                        !notUpdated.Contains(c)
                        &&
                        targetVals.ContainsKey(c.Key)
                    );

                    var yy = sourceVals.Values.Where(x => x.Value.Contains("System.Draw")).Concat(targetVals.Values.Where(x => x.Value.Contains("System.Draw")));
                    if (yy.Count() != 0)
                    {
                    }

                    reportRoot.Add(
                        new XElement("file",
                            new XAttribute("filename", inf.TargetFile),
                            new XElement("missing",
                                from x in missing
                                select new XElement("item",
                                    new XAttribute("name", x.Key),
                                    x.Value.Element("value").Value
                                )
                            ),
                            new XElement("unused",
                                from x in unused
                                select new XElement("item",
                                    new XAttribute("name", x.Key),
                                    x.Value.Element("value").Value
                                )
                            ),
                            new XElement("not-updated",
                                from x in notUpdated
                                select new XElement("item",
                                    new XAttribute("name", x.Key),
                                    x.Value.Element("value").Value
                                )
                            ),
                            new XElement("updated",
                                from x in updated
                                select new XElement("item",
                                    new XAttribute("name", x.Key),
                                    new XElement("original", 
                                        x.Value.Element("value").Value
                                    ),
                                    new XElement("translated",
                                        targetVals[x.Key].Element("value").Value
                                    )
                                )
                            )
                        )
                    );
                }

                report.Save(System.IO.Path.Combine(Application.StartupPath, "report." + culture + ".xml"));
            }
        }

        private static void Create(string culture)
        {
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.GetCultureInfo(culture); //Validate
            string folder = System.IO.Path.Combine(Application.StartupPath, culture);
            if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);
            Update(culture);
        }

        public class ResXFileInfo
        {
            public string SourceFile { get; set; }
            public string TargetFile { get; set; }
            public string NetrualTargetFile { get; set; }
            public bool IsForm { get; set; }
        }

        public static IEnumerable<ResXFileInfo> GetResXList(string culture)
        {
            List<ResXFileInfo> res = new List<ResXFileInfo>();
            foreach (XElement conf in XDocument.Load(System.IO.Path.Combine(Application.StartupPath, "configuration.xml")).Element("root").Elements("configuration"))
            {
				string sourcefolder = conf.Element("sourcefolder").Value;
				if (Duplicati.Library.Utility.Utility.IsClientLinux)
					sourcefolder = sourcefolder.Replace("\\", System.IO.Path.DirectorySeparatorChar.ToString());

				string outputfolder = System.IO.Path.GetFullPath(Duplicati.Library.Utility.Utility.AppendDirSeparator(System.IO.Path.Combine(Application.StartupPath, culture)));
                sourcefolder = Duplicati.Library.Utility.Utility.AppendDirSeparator(System.IO.Path.GetFullPath(sourcefolder));

                foreach (XElement fn in conf.Elements("assembly"))
                {
					string subfolder = fn.Attribute("folder").Value;
					if (Duplicati.Library.Utility.Utility.IsClientLinux)
						subfolder = subfolder.Replace("\\", System.IO.Path.DirectorySeparatorChar.ToString());
					
                    foreach (string s in Duplicati.Library.Utility.Utility.EnumerateFiles(System.IO.Path.Combine(sourcefolder, subfolder)))
                    {
                        if (s.ToLower().StartsWith(Application.StartupPath.ToLower()))
                            continue;

                        if (s.EndsWith(".resx"))
                        {
                            string targetNameNeutral = System.IO.Path.Combine(outputfolder, s.Substring(sourcefolder.Length));

                            if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(targetNameNeutral)))
                                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetNameNeutral));
                            
                            string targetFilename = targetNameNeutral.Substring(0, targetNameNeutral.Length - "resx".Length) + culture + ".resx";
                            if (res.Where(x => x.TargetFile == targetFilename).FirstOrDefault() == null)
                                res.Add(new ResXFileInfo() 
                                {
                                    SourceFile = s,
                                    IsForm = System.IO.File.Exists(System.IO.Path.ChangeExtension(s, ".cs")),
                                    NetrualTargetFile = targetNameNeutral,
                                    TargetFile = targetFilename
                                });
                        }
                    }
                }
            }

            return res;
        }

        private static void Update(string cultures)
        {
            foreach (string culture in GetLocaleFolders(cultures))
            {
                foreach (ResXFileInfo inf in GetResXList(culture))
                {
                    if (inf.IsForm)
                        System.IO.File.Copy(inf.SourceFile, inf.NetrualTargetFile, true); //Copy the updated resx

                    if (System.IO.File.Exists(inf.TargetFile))
                    {
                        //Merge, forms are auto-merged, in that they depend on the neutral .resx file
                        if (!inf.IsForm)
                        {
                            XDocument targetDoc = XDocument.Load(inf.TargetFile);
                            XNode insertTarget = targetDoc.Element("root").LastNode;

                            var sourceVals = XDocument.Load(inf.SourceFile).Element("root").Elements("data").ToSafeDictionary(c => c.Attribute("name").Value, inf.SourceFile);
                            var targetVals = targetDoc.Element("root").Elements("data").ToSafeDictionary(c => c.Attribute("name").Value, inf.TargetFile);

                            bool updated = false;
                            foreach (var item in sourceVals)
                                if (!targetVals.ContainsKey(item.Key))
                                {
                                    updated = true;
                                    insertTarget.AddAfterSelf(new XElement(item.Value));
                                }

                            if (updated)
                                targetDoc.Save(inf.TargetFile);
                        }
                    }
                    else
                    {
                        if (inf.IsForm)
                            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(inf.TargetFile))
                                sw.Write(Properties.Resources.Empty_resx);
                        else
						{
							if (Duplicati.Library.Utility.Utility.IsClientLinux)
							{
								var doc = XDocument.Load(inf.SourceFile);
								var entries = from x in doc.Element("root").Elements("data")
                                              where
                                              x.Attribute("type") != null
                                              &&
                                              x.Attribute("type").Value == "System.Resources.ResXFileRef, System.Windows.Forms"
                                              select x;
						
								foreach(var e in entries)
								{
									string name = e.Element("value").Value;
									int ixpos = name.IndexOf(";");
									string path = name.Substring(0, ixpos); 
									path = path.Replace ("\\", System.IO.Path.DirectorySeparatorChar.ToString());
									e.Element("value").Value = path + name.Substring(ixpos);
								}
								
								doc.Save(inf.TargetFile);
						
							} else {
                            	System.IO.File.Copy(inf.SourceFile, inf.TargetFile);
							}
						}
                    }
					

                    foreach (var item in from y in
                                             (from x in XDocument.Load(inf.SourceFile).Element("root").Elements("data")
                                              where
                                              x.Attribute("type") != null
                                              &&
                                              x.Attribute("type").Value == "System.Resources.ResXFileRef, System.Windows.Forms"
                                              select x)
                                         let relname = y.Element("value").Value.Substring(0, y.Element("value").Value.IndexOf(";"))
                                         let sourceRes = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(inf.SourceFile), relname)).Replace("\\", System.IO.Path.DirectorySeparatorChar.ToString())
                                         let targetRes = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(inf.TargetFile), relname)).Replace("\\", System.IO.Path.DirectorySeparatorChar.ToString())

                                         select new { sourceRes, targetRes }
                                           )
                    {
                        if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(item.targetRes)))
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(item.targetRes));

                        if (!System.IO.File.Exists(item.targetRes))
						{
							if (Duplicati.Library.Utility.Utility.IsClientLinux && !System.IO.File.Exists(item.sourceRes)) 
							{
								string basefolder = System.IO.Path.GetDirectoryName(item.sourceRes);
								string sourcefile = System.IO.Directory.GetFiles (basefolder).FirstOrDefault (x => item.sourceRes.Equals(x, StringComparison.InvariantCultureIgnoreCase));
								if (sourcefile == null)
									Console.WriteLine ("Source file not found: " + item.sourceRes);
								else	
									System.IO.File.Copy (sourcefile, item.targetRes);
								
							} else {
                            	System.IO.File.Copy(item.sourceRes, item.targetRes);
							}
						}
                    }

                }

                string batchfilename = System.IO.Path.Combine(System.IO.Path.Combine(Application.StartupPath, culture), "gui update.bat");
                System.IO.File.WriteAllText(batchfilename, Properties.Resources.Batchjob_bat, System.Text.Encoding.Default);
            }
        }
                


        private static void Clean(string cultureReq)
        {
            string root = Duplicati.Library.Utility.Utility.AppendDirSeparator(Application.StartupPath);
            if (!string.IsNullOrEmpty(cultureReq))
                root = System.IO.Path.Combine(root, cultureReq);

            foreach (string s in Duplicati.Library.Utility.Utility.EnumerateFiles(Application.StartupPath))
                if (System.IO.Path.GetDirectoryName(s) != Application.StartupPath && (System.IO.Path.GetExtension(s) == ".resources" || System.IO.Path.GetExtension(s) == ".dll"))
                    System.IO.File.Delete(s);
                else if (System.IO.Path.GetExtension(s) == ".resx")
                {
                    //Remove the extra files required for resgen.exe
                    string culture = s.Substring(root.Length);
                    culture = culture.Substring(0, culture.IndexOf(System.IO.Path.DirectorySeparatorChar));
                    if (!s.ToLower().EndsWith("." + culture.ToLower() + ".resx") && System.IO.File.Exists(s.Substring(0, s.Length - "resx".Length) + culture + ".resx"))
                        System.IO.File.Delete(s);
                }
                
        }

        private static void Compile(string cultureReq)
        {
            XDocument doc = XDocument.Load(System.IO.Path.Combine(Application.StartupPath, "configuration.xml"));

            foreach (XElement conf in doc.Element("root").Elements("configuration"))
            {
                string keyfile = conf.Element("keyfile") == null ? null : conf.Element("keyfile").Value;
                string versionassembly = conf.Element("versionassembly").Value;
                string outputfolder = conf.Element("outputfolder").Value;
                string productname = conf.Element("productname").Value;

                foreach (XElement n in conf.Elements("assembly"))
                {
                    string assemblyName = n.Attribute("name").Value;
                    string folder = n.Attribute("folder").Value;
                    string @namespace = n.Attribute("namespace") == null ? assemblyName : n.Attribute("namespace").Value;

                    foreach (string culture in GetLocaleFolders(cultureReq))
                    {
                        List<string> excludes = (from x in n.Elements("exclude")
                                                 let fullpath = System.IO.Path.Combine(System.IO.Path.Combine(Application.StartupPath, culture), x.Value)
                                                 select fullpath).ToList();

                        string outfolder = System.IO.Path.GetFullPath(System.IO.Path.Combine(outputfolder, culture));
                        if (!System.IO.Directory.Exists(outfolder))
                            System.IO.Directory.CreateDirectory(outfolder);

                        ResXCompiler.CompileResxFiles(System.IO.Path.Combine(System.IO.Path.Combine(Application.StartupPath, culture), folder), excludes, @namespace, System.IO.Path.Combine(outfolder, assemblyName + ".resources.dll"), System.IO.Path.GetFullPath(versionassembly), keyfile, culture, productname);
                    }
                }
            }
        }
    }
}