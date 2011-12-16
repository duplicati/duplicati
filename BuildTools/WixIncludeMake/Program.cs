using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace WixIncludeMake
{
	public class Program
	{
		private const string DB_FILE = "generated-keys.xml";
		private const string USER_FILE = "fixed-keys.xml";
		private static readonly string DIR_SEP = System.IO.Path.DirectorySeparatorChar.ToString();
		private static readonly string basedir = System.IO.Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly().Location);
		
		public static readonly XNamespace XWIX = "http://schemas.microsoft.com/wix/2006/wi";
		
		private interface IFolder
		{
			Dictionary<string, FolderInfo> Folders { get; }
			Dictionary<string, FileInfo> Files { get; }
		}
				
		private class FolderInfo : IFolder
		{
			public Dictionary<string, FolderInfo> Folders { get; private set; }
			public Dictionary<string, FileInfo> Files { get; private set; }
			public string Name { get; set; }
			private string m_id;
			public string ID { get { return m_id.Replace(" ", "").Replace("-", "").ToUpperInvariant(); }  set { m_id = value; } }
			public Guid GUID { get; set; }
			
			private FolderInfo()
			{
				Folders = new Dictionary<string, FolderInfo>();
				Files = new Dictionary<string, FileInfo>();
			}
			
			public FolderInfo(string name, string id, Guid guid)
				: this()
			{
				this.Name = name;
				this.ID = id;
				this.GUID = guid;
			}
			
			public IEnumerable<XElement> FileNodes
			{
				get
				{
					return 
						from x in Files 
						orderby x.Key 
						select 
							new XElement(XWIX + "File",
		                    	new XAttribute("Id", "file_" + x.Value.ID),
							    new XAttribute("Checksum", "yes"),
							    new XAttribute("Source", x.Value.SourceFile)
							);
				}
			}
			
			public XElement FolderNode
			{
				get
				{
					return 
						new XElement(XWIX + "Directory",
	                    	new XAttribute("Id", "dir_" + this.ID),
						    new XAttribute("Name", this.Name),
						    new XElement(XWIX + "Component",
								new XAttribute("Id", "comp_" + this.ID),
								new XAttribute("DiskId", 1),
								new XAttribute("KeyPath", "yes"),
								new XAttribute("Guid", this.GUID.ToString()),
							    new XAttribute("Win64", "$(var.Win64)"),
						        FileNodes
				            ),
    						from x in Folders 
							orderby x.Key 
							select x.Value.FolderNode
						);

				}
			}
			
			public IEnumerable<FolderInfo> Subfolders
			{
				get 
				{
					List<FolderInfo> res = new List<FolderInfo>();
					res.Add (this);
					foreach(var f in this.Folders.Values)
						res.AddRange(f.Subfolders);
					return res;
				}
			}
		}
		
		private class FileInfo
		{
			public string Name { get; set; }
			private string m_id;
			public string ID { get { return m_id.Replace(" ", "").Replace("-", "").ToUpperInvariant(); }  set { m_id = value; } }
			public string SourceFile { get; set; }
			
			public FileInfo(string name, string id, string sourcefile)
			{
				this.Name = name;
				this.ID = id;
				this.SourceFile = sourcefile;
			}
		}
		
		private class RootFolderInfo : FolderInfo
		{
			public Dictionary<string, string> GeneratedKeys { get; private set; }
			public Dictionary<string, string> UserKeys { get; private set; }
			public string ComponentID { get; set; }
			private readonly Options m_opt;
						
			public RootFolderInfo(Options opt, IEnumerable<string> paths = null)
				: base("", "", Guid.NewGuid())
			{
				GeneratedKeys = ReadKeyDatabase(opt.dbfilename);
				UserKeys = ReadKeyDatabase(opt.userfilename);
				ComponentID = opt.componentid;
				m_opt = opt;
				
				string id;
				if (!GeneratedKeys.TryGetValue(DIR_SEP, out id))
					id = Guid.NewGuid().ToString();
				
				if (UserKeys.ContainsKey(DIR_SEP))
					id = UserKeys[DIR_SEP];
				
				this.ID = id;
				
				Add (paths);
			}
			
			public void Add(IEnumerable<string> paths)
			{
				if (paths == null)
					return;
				
				foreach(var p in paths)
					Add (p);
			}
			
			public void Add(string path)
			{
				if (string.IsNullOrEmpty(path))
					return;
				
				bool isDir = false;
				if (path.EndsWith(DIR_SEP))
				{
					isDir = true;
					path = path.Substring(0, path.Length - 1);
				}
				
				List<string> folders = new List<string>();
				string filename = null;

				while(!string.IsNullOrEmpty(path))
				{
					string sourcepath = System.IO.Path.GetDirectoryName(path);
					string elname = System.IO.Path.GetFileName(path);
					folders.Add(elname);
					
					path = sourcepath;
					if (path.EndsWith(DIR_SEP))
						path = path.Substring(0, path.Length - 1);

				}
				
				if (!isDir)
				{
					filename = folders[0];
					folders.RemoveAt (0);
				}
				
				IFolder cur = this;
				folders.Reverse();
				
				StringBuilder sbp = new StringBuilder();
				
				foreach(var f in folders)
				{
					sbp.Append(f);
					sbp.Append(DIR_SEP);
					
					FolderInfo next;
					if (!cur.Folders.TryGetValue(f, out next))
					{
						//We have a new folder
						string id;
						if (!GeneratedKeys.TryGetValue(sbp.ToString(), out id))
						{
							id = Guid.NewGuid().ToString();
							GeneratedKeys.Add(sbp.ToString(), id);
						}
						
						Guid g;
						try { g = new Guid(id); }
						catch 
						{
							g = Guid.NewGuid();
							GeneratedKeys[sbp.ToString()] = g.ToString();
							Console.Error.WriteLine ("Unable to parse {0} into a GUID, I told you not to edit the file!!!{1}A new GUID will be used: {2}", id, Environment.NewLine, g);
						}
						
						string userkey;
						if (!UserKeys.TryGetValue(sbp.ToString(), out userkey))
							userkey = g.ToString();
						
						next = new FolderInfo(f, userkey, g);
						cur.Folders.Add(f, next);
					}
					
					cur = next;
				}
				
				if (!string.IsNullOrEmpty(filename)) 
				{
					sbp.Append(filename);
					
					string id;
					if (!GeneratedKeys.TryGetValue(sbp.ToString(), out id))
						GeneratedKeys.Add(sbp.ToString(), id = Guid.NewGuid().ToString());
					if (UserKeys.ContainsKey(sbp.ToString()))
					    id = UserKeys[sbp.ToString()];
					
					string fullpath = System.IO.Path.Combine(m_opt.fileprefix, sbp.ToString()).Replace(DIR_SEP, "\\");
					
					cur.Files.Add(filename, new FileInfo(filename, id, fullpath));
				}
			}
			
			public IEnumerable<XElement> Node
			{
				get
				{
					List<XElement> res = new List<XElement>();
					res.Add(new XElement(XWIX + "Component",
							new XAttribute("Id", "comp_" + this.ID),
							new XAttribute("DiskId", 1),
							new XAttribute("KeyPath", "yes"),
							new XAttribute("Guid", this.GUID.ToString()),
						    new XAttribute("Win64", "$(var.Win64)"),						             
						    FileNodes
						));
					
					res.AddRange(
						from x in Folders 
						orderby x.Key 
						select x.Value.FolderNode
					);
					
					return res;
				}
			}
						
			public XElement GroupNode
			{
				get
				{
					return new XElement(XWIX + "ComponentGroup",
	                    new XAttribute("Id", this.ComponentID),
                    	from x in this.Subfolders
	                    where x != this
					    select new XElement(XWIX + "ComponentRef",
					    	new XAttribute("Id", "comp_" + x.ID)
					    )
					);                   
				}
			}
		}
		
		public static string GuidToString(Guid g)
		{
			StringBuilder sb = new StringBuilder();
			foreach(var b in g.ToByteArray())
				sb.Append(string.Format("{0:x2}", b));
			return sb.ToString();
		}
		
		private class Options
		{
			public string dbfilename = System.IO.Path.Combine(basedir, DB_FILE);
			public string userfilename = System.IO.Path.Combine(basedir, USER_FILE);
			public string sourcefolder = System.IO.Path.GetFullPath(Environment.CurrentDirectory);
			public string outputfile = System.IO.Path.GetFullPath("binfiles.wxs");
			public string ignorefilter = null;
			public string dirref = "INSTALLLOCATION";
			public string componentid = "FILES_COMPONENT";
			public string fileprefix = "";
			
			public void Fixup()
			{
				sourcefolder = System.IO.Path.GetFullPath(sourcefolder);
				outputfile = System.IO.Path.GetFullPath(outputfile);
				sourcefolder = Duplicati.Library.Utility.Utility.AppendDirSeparator(sourcefolder);
			}
		}
		
		public static void Main (string[] _args)
		{			
			List<string> args = new List<string>(_args);
			Dictionary<string, string> options = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(args);
			Options opt = new Options();
			
			
			foreach(FieldInfo fi in opt.GetType().GetFields())
				if (options.ContainsKey(fi.Name))
					fi.SetValue(opt, options[fi.Name]);
			
			opt.Fixup();			
			
			Duplicati.Library.Utility.FilenameFilter filter = null;
			if (!string.IsNullOrEmpty(opt.ignorefilter))
				filter = new Duplicati.Library.Utility.FilenameFilter(
					new List<Duplicati.Library.Utility.IFilenameFilter>(
						new Duplicati.Library.Utility.IFilenameFilter[] {
							new Duplicati.Library.Utility.RegularExpressionFilter(false, opt.ignorefilter)
						}
					)
				);
			
			
			var paths = Duplicati.Library.Utility.Utility.EnumerateFileSystemEntries(opt.sourcefolder, filter).ConvertAll(x => x.Substring(opt.sourcefolder.Length));

			//A bit backwards, but we have flattend the file list, and now we re-construct the tree,
			// but we do not care much about performance here
			RootFolderInfo rootfolder = new RootFolderInfo(opt, paths);
			
			new XDocument(
				new XElement(XWIX + "Wix",
					new XElement(XWIX + "Fragment",
			            rootfolder.GroupNode,
						new XElement(XWIX + "DirectoryRef",
							new XAttribute("Id", opt.dirref),
			             	rootfolder.Node
						)
					)
				)
			).Save(opt.outputfile);

			WriteKeyDatabase(rootfolder.GeneratedKeys, opt.dbfilename, true);
			
			Console.WriteLine("Generated wxs: {0}", opt.outputfile);
			
		}
				
		private static Dictionary<string, string> ReadKeyDatabase(string filename)
		{			
			if (!System.IO.File.Exists(filename))
				return new Dictionary<string, string>();
			
			return XDocument.Load(filename).Element("root").Elements("entry").ToSafeDictionary(
				c => c.Attribute("name").Value.Replace("\\", DIR_SEP), c => c.Attribute("key").Value, filename); 
		}
		
		private static void WriteKeyDatabase(Dictionary<string, string> keys, string filename, bool isKeyDb)
		{
			new XDocument(
				new XComment(isKeyDb ? "This file is autogenerated, do not edit! EVER!" : "format is <entry name=\"name\" key=\"key\" />"),
				new XElement("root",
					from x in keys
					select new XElement("entry", 
						new XAttribute("name", x.Key.Replace(DIR_SEP, "\\")),
						new XAttribute("key", x.Value)
					)
				)
			).Save(filename);
		} 
	}
}
