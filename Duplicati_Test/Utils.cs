//  Copyright (C) 2012, Aaron Hamid
//  https://github.com/ahamid, aaron.hamid@gmail.com
//  
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
// 
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using NUnit.Framework;
using Duplicati.Library.Utility;
using System.Data.LightDatamodel;

namespace Duplicati_Test {

    // assert helpers
    internal static class AssertHelper {
        internal static void Throws(Action action, string message = null)
        {
            Throws(typeof(Exception), action, message);
        }

        internal static void Throws(Type type, Action action, string message = null)
        {
            try
            {
                action();
                Assert.Fail("Exception of type " + type + " not thrown");
            }
            catch (Exception e)
            {
                Assert.IsTrue(type.IsAssignableFrom(e.GetType()), "Expected exception type {0} is not assignable from actual exception type {1}", new string[] { type.FullName, e.GetType().FullName });
                if (message != null)
                {
                    Assert.IsTrue(e.Message.Contains(message), "Actual exception message \"{0}\" does not contain expected string \"{1}\"", new string[] { e.Message, message });
                }
            }
        }
        
        internal static void AssertFilesMatch(string dir, string[] files)
        {
            foreach (string file in files)
            {
                var dst_file = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(file));
                // backed-up file should be restored
                Assert.IsTrue(System.IO.File.Exists(dst_file), "Expected destination file {0} does not exist", dst_file);
                Assert.AreEqual(Utils.MD5Sum(file), Utils.MD5Sum(dst_file));
            }
        }
    }
    
    internal static class Utils {
        internal const string TEST_DB = "Duplicati_Test.sqlite";
        
        internal static byte[] MD5Sum(string filename)
        {
            using (var md5 = MD5.Create())
            using (var stream = System.IO.File.OpenRead(filename))
            {
                return md5.ComputeHash(stream);
            }
        }
        
        internal static TempFile CreateTempFile(string dir) {
            return new TempFile(System.IO.Path.Combine(dir, Guid.NewGuid().ToString()));
        }
        
        internal static IList<TempFile> GenerateFiles(string dir, params string[] contents)
        {
            List<TempFile> files = new List<TempFile>();
            foreach (string content in contents) {
                TempFile file = CreateTempFile(dir);
                System.IO.File.WriteAllText(file, content);
                files.Add(file);
            }
            return files;
        }

        internal static System.Data.IDbConnection CreateDbConnection() {
            return (System.Data.IDbConnection) Activator.CreateInstance(Duplicati.Server.SQLiteLoader.SQLiteConnectionType);
        }
        
        internal static IDataFetcherWithRelations InitializeDbConnection(TempFolder tempFolder, System.Data.IDbConnection con) {
            Duplicati.GUI.Program.OpenSettingsDatabase(con, tempFolder, TEST_DB);
            Duplicati.GUI.Program.DataConnection = new DataFetcherWithRelations(new SQLiteDataProvider(con));
            return Duplicati.GUI.Program.DataConnection;
        }
    }
}