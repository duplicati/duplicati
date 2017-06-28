using System;
using System.IO;

namespace SQLiteTool
{
	class MainClass
	{
		public static void Main(string[] args)
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
