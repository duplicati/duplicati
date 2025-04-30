// Copyright (C) 2025, The Duplicati Team
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
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;

namespace ZipFileDebugger
{
    class Program
    {
        static void Main(string[] args)
        {
            var argslist = new List<string>(args);
            var opts = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(argslist, null);

            if (argslist == null || argslist.Count == 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("{0} <filename.zip>", System.Reflection.Assembly.GetEntryAssembly().Location);
                return;
            }

            var filecount = 0;
            var errorcount = 0;
            foreach (var file in argslist)
            {
                if (!File.Exists(file))
                {
                    Console.WriteLine("File not found: {0}", file);
                    continue;
                }

                if (new FileInfo(file).Length == 0)
                {
                    Console.WriteLine("File is emtpy: {0}", file);
                    continue;
                }

                Console.WriteLine("Opening ZIP file {0}", file);

                var errors = false;

                try
                {
                    filecount++;
                    using (var filestream = System.IO.File.Open(file, FileMode.Open, FileAccess.Read,
                        FileShare.None))
                    {
                        using (var zr = new Duplicati.Library.Compression.FileArchiveZip(filestream, ArchiveMode.Read, opts))
                        {
                            var files = zr.ListFilesWithSize(null).ToArray();
                            Console.WriteLine("Found {0} files in archive, testing read-ability for each",
                                files.Length);

                            for (var i = 0; i < files.Length; i++)
                            {
                                Console.Write("Opening file #{0} - {1}", i + 1, files[i].Key);
                                try
                                {
                                    using (var ms = new MemoryStream())
                                    using (var sr = zr.OpenRead(files[i].Key))
                                    {
                                        sr.CopyTo(ms);

                                        if (ms.Length == files[i].Value)
                                            Console.WriteLine(" -- success");
                                        else
                                            Console.WriteLine(" -- bad length: {0} vs {1}", ms.Length, files[i].Value);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    errors = true;
                                    Console.WriteLine(" -- failed: {0}", ex);
                                }

                            }
                        }
                    }

                    if (errors)
                    {
                        Console.WriteLine("Found errors while processing file {0}", file);
                        errorcount++;
                    }
					else
                        Console.WriteLine("Processed file {0} with no errors", file);
                }
                catch (Exception ex)
                {
                    errorcount++;
                    Console.WriteLine("Open failed: {0}", ex);
                }
            }

            Console.Write("Processed {0} ZIP files", filecount);
            if (errorcount == 0)
                Console.WriteLine(" without errors");
            else
                Console.WriteLine(" and found {0} errors", errorcount);
        }
    }
}
