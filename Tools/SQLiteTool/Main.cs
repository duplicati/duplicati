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
using System.IO;

namespace SQLiteTool
{
	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length != 2)
			{
				Console.WriteLine("Usage: ");
				Console.WriteLine("    SQLiteTool <dbpath> <query>");
				Console.WriteLine(" <query> can be the path to a file with SQL statements or an SQL statement");
				return;
			}
	
            using (var connection = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType))
			{
                if ((args[0] ?? string.Empty).IndexOf("Data Source", StringComparison.OrdinalIgnoreCase) >= 0)
                    connection.ConnectionString = args[0];
                else
				    connection.ConnectionString = "Data Source=" + args[0];
                
                Console.WriteLine("Opening with connection string: {0}", connection.ConnectionString);

				connection.Open();
				
                var query = args[1] ?? string.Empty;
				try 
                {
                    if (query.IndexOfAny(Path.GetInvalidPathChars()) < 0 && File.Exists(query))
                        query = System.IO.File.ReadAllText(args[1]); 
                }
				catch {}
				
				var begin = DateTime.Now;
				
				using(var cmd = connection.CreateCommand())
				{
					cmd.CommandText = query;
                    Console.WriteLine("Setting query: {0}", cmd.CommandText);
					using (var rd = cmd.ExecuteReader())
					{
						Console.WriteLine("Execution took: {0:mm\\:ss\\.fff}", DateTime.Now - begin);
					
						for (int i = 0; i < rd.FieldCount; i++)
							Console.Write((i == 0 ? "" : "\t") + rd.GetName(i));
						Console.WriteLine();
						
						long n = 0;
						while (rd.Read())
						{
							for (int i = 0; i < rd.FieldCount; i++)
								Console.Write(string.Format((i == 0 ? "{0}" : "\t{0}"), rd.GetValue(i)));
							Console.WriteLine();
							n++;
						}
						Console.WriteLine("{0} records", n);
					}
				}
			}
		}
	}
}
