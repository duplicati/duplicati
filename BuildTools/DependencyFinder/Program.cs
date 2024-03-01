// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using Mono.Cecil;
using System.IO;
using System.Linq;

namespace DependencyFinder
{
	class MainClass
	{
		public static void Main(string[] args)
		{
			var lst = 
				Directory.EnumerateFiles(args[0], "*.exe", SearchOption.AllDirectories).Union(
					Directory.EnumerateFiles(args[0], "*.dll", SearchOption.AllDirectories))
					.Select(x => {
						try { return AssemblyDefinition.ReadAssembly(x); }
						catch { return null; }
					}).Where(x => x != null)
					.Distinct();

			PoC(lst, Console.Out, new [] { args[0] });
			
		}

		//Adapted from: https://stackoverflow.com/questions/9262464/tool-to-show-assembly-dependencies
		public static void PoC(IEnumerable<AssemblyDefinition> assemblies, TextWriter writer, IEnumerable<string> searchfolders)
		{
			var resolver = new DefaultAssemblyResolver();
			searchfolders.ToList().ForEach(x => resolver.AddSearchDirectory(x));

			//writer.WriteLine("digraph Dependencies {");
			var loaded = assemblies
				.SelectMany(a => a.Modules.Cast<ModuleDefinition>())
				.SelectMany(m => m.AssemblyReferences.Cast<AssemblyNameReference>())
				.Distinct()
				.Select(asm => {
					var dllname = asm.Name + ".dll";
					//Console.WriteLine("Probing for {0}", dllname);
					try { return AssemblyDefinition.ReadAssembly(dllname); }
					catch { } 
					try { return resolver.Resolve(asm.FullName); }
					catch { }

					return null;
				})
				.Where(assembly => assembly != null)
				.ToList();

			//loaded.ForEach(a => a.MainModule.ReadSymbols());

			loaded.Select(x => x.FullName).Distinct().OrderBy(x => x).ToList().ForEach(x => writer.WriteLine("{0}", x));
			/*loaded.ForEach(a =>
				{
					foreach (var r in a.MainModule.AssemblyReferences.Cast<AssemblyNameReference>())
						writer.WriteLine(@"""{0}"" -> ""{1}"";", r.Name, a.Name.Name);
				} );*/

			//writer.WriteLine("}");
		}


	}
}
