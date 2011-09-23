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
using System.Collections.Generic;
using System.Text;
using System.Data;

namespace Duplicati.GUI
{
    /// <summary>
    /// This class will read embedded files from the given folder.
    /// Updates should have the form &quot;1.Sample upgrade.sql&quot;.
    /// When the database schema changes, simply put a new file into the folder.
    /// Each upgrade file should ONLY upgrade from the previous version.
    /// If done correctly, a user may be upgrade from the very first version
    /// to the very latest.
    /// 
    /// The Schema.sql file should ALWAYS have the latests schema, as that will 
    /// ensure that new installs do not run upgrades after installation.
    /// Also remember to update the last line in Schema.sql to insert the 
    /// current version number in the version table.
    /// 
    /// Currently no upgrades may contain semicolons, except as the SQL statement 
    /// delimiter.
    /// </summary>
    static class DatabaseUpgrader
    {
        //This is the "folder" where the embedded resources can be found
        private const string FOLDER_NAME = "Database_schema";
        
        //This is the name of the schema sql
        private const string SCHEMA_NAME = "Schema.sql";

        /// <summary>
        /// Ensures that the database is up to date
        /// </summary>
        /// <param name="connection">The database connection to use</param>
        /// <param name="sourcefile">The file the database is placed in</param>
        public static void UpgradeDatabase(IDbConnection connection, string sourcefile)
        {
            //Shorthand for current assembly
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();

            if (connection.State != ConnectionState.Open)
            {
                if (string.IsNullOrEmpty(connection.ConnectionString))
                    connection.ConnectionString = "Data Source=" + sourcefile;

                connection.Open();
            }


            int dbversion = 0;
            IDbCommand cmd = connection.CreateCommand();
            try
            {
                //See if the version table is present,
                cmd.CommandText = "SELECT COUNT(*) FROM SQLITE_MASTER WHERE Name LIKE 'Version'";

                int count = Convert.ToInt32(cmd.ExecuteScalar());

                if (count == 0)
                    dbversion = -1; //Empty
                else if (count == 1)
                {
                    cmd.CommandText = "SELECT max(Version) FROM Version";
                    dbversion = Convert.ToInt32(cmd.ExecuteScalar());
                }
                else
                    throw new Exception(Strings.DatabaseUpgrader.TableLayoutError);

            }
            catch(Exception ex)
            {
                //Hopefully a more explanatory error message
                throw new Exception(string.Format(Strings.DatabaseUpgrader.DatabaseFormatError, ex.Message), ex);
            }


            //On a new database, we just load the most current schema, and upgrade from there
            //This avoids potentitally lenghty upgrades
            if (dbversion == -1)
            {
                cmd.CommandText = new System.IO.StreamReader(asm.GetManifestResourceStream(typeof(DatabaseUpgrader), FOLDER_NAME + "." + SCHEMA_NAME)).ReadToEnd();
                cmd.ExecuteNonQuery();
                UpgradeDatabase(connection, sourcefile);
                return;
            }

            //Get updates, and sort them according to version
            //This enables upgrading through several versions
            //ea, from 1 to 8, by stepping 2->3->4->5->6->7->8
            SortedDictionary<int, string> upgrades = new SortedDictionary<int, string>();
            string prefix = typeof(DatabaseUpgrader).Namespace + "." + FOLDER_NAME + ".";
            int maxversion = -1;
            foreach (string s in asm.GetManifestResourceNames())
            {
                //The resource name will be "Duplicati.GUI.Database_schema.1.Sample upgrade.sql"
                //The number indicates the version that will be upgraded to
                if (s.StartsWith(prefix) && !s.Equals(prefix + SCHEMA_NAME))
                {
                    try
                    {
                        string version = s.Substring(prefix.Length, s.IndexOf(".", prefix.Length + 1) - prefix.Length);
                        int fileversion = int.Parse(version);
                        maxversion = Math.Max(maxversion, fileversion);
                        if (fileversion > dbversion)
                        {
                            if (!upgrades.ContainsKey(fileversion))
                                upgrades.Add(fileversion, "");
                            upgrades[fileversion] += new System.IO.StreamReader(asm.GetManifestResourceStream(s)).ReadToEnd();
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (dbversion > maxversion)
                throw new Exception(string.Format(Strings.DatabaseUpgrader.InvalidVersionError, dbversion, maxversion, System.IO.Path.GetDirectoryName(sourcefile)));

            if (upgrades.Count > 0)
            {
                string backupfile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sourcefile), Strings.DatabaseUpgrader.BackupFilenamePrefix + " " + DateTime.Now.ToString("yyyyMMddhhmmss") + ".sqlite");

                try
                {
                    if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(backupfile)))
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backupfile));

                    //Keep a backup
                    System.IO.File.Copy(sourcefile, backupfile, false);

                    int newversion = dbversion;
                    foreach (int key in upgrades.Keys)
                    {
                        //TODO: Find a better way to split SQL statements, as there may be embedded semicolons
                        //in the SQL, like "UPDATE x WHERE y = ';';"

                        //We split them to get a better error message
                        foreach(string c in upgrades[key].Split(';'))
                            if (c.Trim().Length > 0)
                            {
                                cmd.CommandText = c;
                                cmd.ExecuteNonQuery();
                            }

                        newversion = Math.Max(newversion, key);
                    }

                    //Update databaseversion, so we don't run the scripts again
                    cmd.CommandText = "Update version SET Version = " + newversion.ToString();
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    connection.Close();
                    //Restore the database
                    System.IO.File.Copy(backupfile, sourcefile, true);
                    throw new Exception(string.Format(Strings.DatabaseUpgrader.UpgradeFailure, cmd.CommandText, ex.Message), ex);
                }
            }


        }
    }
}
