using System;
using System.IO;
using System.Linq;
using Duplicati.Library.Common;
using Duplicati.Library.Common.IO;
using Duplicati.Library.SQLiteHelper;

namespace SQLiteDecryptTool
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Duplicati Database Decryption Tool");
            Console.WriteLine();
            Console.WriteLine("Duplicati version 2.0 used a weak RC4 encryption algorithm to encrypt the database.");
            Console.WriteLine("The RC4 and other methods are no longer supported for SQLite with the native libraries");
            Console.WriteLine("The encryption support was primarily available on Windows, but could have been available on other platforms.");
            Console.WriteLine("This tool will decrypt the database and save a backup before attempting to decrypt it.");
            Console.WriteLine();

            var helpargs = new string[] { "-h", "--help", "/?", "/h", "/help", "help" };

            string database;
            string password = null;
            if (args.Length == 0)
            {
                var dataFolder = DefaultDataFolder();
                var sqlitefile = Path.Combine(dataFolder, "Duplicati-server.sqlite");
                if (File.Exists(sqlitefile))
                {
                    Console.WriteLine($"Found database in default location: {sqlitefile}");
                    Console.Write("Do you want to decrypt this database? (y/n): ");
                    var response = Console.ReadLine();
                    if (response.ToLower() != "y")
                    {
                        Console.WriteLine("Exiting...");
                        return;
                    }
                    database = sqlitefile;
                }
                else
                {
                    Console.WriteLine("Database file not found in default location.");
                    Console.Write("Please enter the path to the database file: ");
                    database = Console.ReadLine();
                }
            }
            else if (args.Length == 1 && !helpargs.Any(x => string.Equals(x, args[0], StringComparison.OrdinalIgnoreCase)))
            {
                database = args[0];
            }
            else if (args.Length == 2)
            {
                database = args[0];
                password = args[1];
            }
            else if (args.Length == 2)
            {
                database = args[0];
                password = args[1];
            }
            else
            {
                Console.WriteLine("Usage: SQLiteDecryptTool [Duplicati-server.sqlite] [password]");
                Console.WriteLine("If no database is provided, the default Duplicati database is used");
                Console.WriteLine("If no password is provided, the default password is used");
                return;
            }

            if (!File.Exists(database))
            {
                Console.WriteLine($"Database file not found: {database}");
                return;
            }

            // TODO: Can we detect if the DB is encryped or not, just by looking at the file?

            string backup = Path.Combine(Path.GetDirectoryName(database), Path.GetFileNameWithoutExtension(database) + "_backup_enc" + Path.GetExtension(database));
            Console.WriteLine($"Creating a copy of the database in {backup}...");
            File.Copy(database, backup);

            Console.WriteLine("Decrypting the database...");
            var con = SQLiteLoader.LoadConnection();
            var setPwdMethod = con.GetType().GetMethod("SetPassword", new[] { typeof(string) });
            var changePwdMethod = con.GetType().GetMethod("ChangePassword", new[] { typeof(string) });
            if (setPwdMethod == null)
            {
                Console.WriteLine("The SQLite native library does not support encryption, cannot decrypt.");
                return;
            }

            if (changePwdMethod == null)
            {
                Console.WriteLine("The SQLite native library does not support changing the password, cannot decrypt.");
                return;
            }

            try
            {
                DecryptDatabase(database, password);
                Console.WriteLine("Database decrypted successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error decrypting database: {ex.Message}");
                return;
            }

            if (Platform.IsClientWindows)
                Console.WriteLine("Press enter to close the program");
        }

        static string DefaultDataFolder()
        {
            // Logic here is copied from Duplicati.Server.Program.GetDefaultDataFolder(),
            // and adapted to not support all the options

            var serverDataFolder = Environment.GetEnvironmentVariable("DUPLICATI_HOME");
            if (string.IsNullOrEmpty(serverDataFolder))
            {
                serverDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Duplicati");
                if (Platform.IsClientWindows)
                {
                    var localappdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Duplicati");

                    var prefile = Path.Combine(serverDataFolder, "Duplicati-server.sqlite");
                    var curfile = Path.Combine(localappdata, "Duplicati-server.sqlite");

                    if (File.Exists(curfile) || !File.Exists(prefile))
                        serverDataFolder = localappdata;
                }

                return serverDataFolder;
            }
            return Util.AppendDirSeparator(Environment.ExpandEnvironmentVariables(serverDataFolder).Trim('"'));
        }

        static void DecryptDatabase(string databasePath, string dbPassword)
        {
            if (string.IsNullOrEmpty(dbPassword))
                dbPassword = Environment.GetEnvironmentVariable("DUPLICATI_DB_KEY");
            if (string.IsNullOrEmpty(dbPassword))
                dbPassword = "Duplicati_Key_42";


            //Create the connection instance
            var con = SQLiteLoader.LoadConnection();

            try
            {
                //Attempt to open the database, disable encrpytion
                SQLiteLoader.OpenDatabase(con, databasePath, false, dbPassword);
            }
            catch (Exception ex)
            {
                //Unwrap the reflection exceptions
                if (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                throw new Exception($"{ex.Message}");
            }
        }
    }
}

