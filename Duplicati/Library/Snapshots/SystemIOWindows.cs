//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
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
using System.Linq;
using System.Collections.Generic;
using System.Security.AccessControl;


namespace Duplicati.Library.Snapshots
{
    public struct SystemIOWindows : ISystemIO
    {
        private const string UNCPREFIX = @"\\?\";
        private static readonly string DIRSEP = System.IO.Path.DirectorySeparatorChar.ToString();

        public static bool IsPathTooLong(string path)
        {
            if (path.StartsWith(UNCPREFIX) || path.Length > 260)
                return true;

            return false;
        }

        public static string PrefixWithUNC(string path)
        {
            if (!path.StartsWith(UNCPREFIX))
                return UNCPREFIX + path;
            else
                return path;
        }

        public static string StripUNCPrefix(string path)
        {
            if (path.StartsWith(UNCPREFIX))
                return path.Substring(UNCPREFIX.Length);
            else
                return path;
        }

        #region ISystemIO implementation
        public void DirectoryDelete(string path)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.Directory.Delete(path);
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.Directory.Delete(PrefixWithUNC(path));
        }

        public void DirectoryCreate(string path)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.Directory.CreateDirectory(path);
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.Directory.CreateDirectory(PrefixWithUNC(path));
        }

        public bool DirectoryExists(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.Directory.Exists(path); }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.Directory.Exists(PrefixWithUNC(path));
        }

        public void FileDelete(string path)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.File.Delete(path);
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.File.Delete(PrefixWithUNC(path));
        }

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.File.SetLastWriteTimeUtc(path, time);
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.File.SetLastWriteTimeUtc(PrefixWithUNC(path), time);
        }

        public void FileSetCreationTimeUtc(string path, DateTime time)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.File.SetCreationTimeUtc(path, time);
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.File.SetCreationTimeUtc(PrefixWithUNC(path), time);
        }

        public DateTime FileGetLastWriteTimeUtc(string path)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    return System.IO.File.GetLastWriteTimeUtc(path);
                }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.GetLastWriteTimeUtc(PrefixWithUNC(path));
        }

        public DateTime FileGetCreationTimeUtc(string path)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    return System.IO.File.GetCreationTimeUtc(path);
                }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.GetCreationTimeUtc(PrefixWithUNC(path));
        }

        public bool FileExists(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.File.Exists(path); }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.Exists(PrefixWithUNC(path));
        }

        public System.IO.Stream FileOpenRead(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite); }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.Open(PrefixWithUNC(path), Alphaleonis.Win32.Filesystem.FileMode.Open, Alphaleonis.Win32.Filesystem.FileAccess.Read, Alphaleonis.Win32.Filesystem.FileShare.ReadWrite);
        }

        public System.IO.Stream FileOpenWrite(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.File.OpenWrite(path); }
                catch (System.IO.PathTooLongException) { }

            if (FileExists(path))
                return Alphaleonis.Win32.Filesystem.File.OpenWrite(PrefixWithUNC(path));
            else
                return FileCreate(path);
        }

        public System.IO.Stream FileOpenReadWrite(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.File.Open(path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.Read); }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.Open(PrefixWithUNC(path), Alphaleonis.Win32.Filesystem.FileMode.OpenOrCreate, Alphaleonis.Win32.Filesystem.FileAccess.ReadWrite, Alphaleonis.Win32.Filesystem.FileShare.Read);
        }

        public System.IO.Stream FileCreate(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.File.Create(path); }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.Create(PrefixWithUNC(path));
        }

        public System.IO.FileAttributes GetFileAttributes(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.File.GetAttributes(path); }
                catch (System.IO.PathTooLongException) { }

            return (System.IO.FileAttributes)Alphaleonis.Win32.Filesystem.File.GetAttributes(PrefixWithUNC(path));
        }

        public void SetFileAttributes(string path, System.IO.FileAttributes attributes)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.File.SetAttributes(path, attributes); 
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.File.SetAttributes(PrefixWithUNC(path), (Alphaleonis.Win32.Filesystem.FileAttributes)attributes);
        }

        public void CreateSymlink(string symlinkfile, string target, bool asDir)
        {
            if (FileExists(symlinkfile) || DirectoryExists(symlinkfile))
                throw new System.IO.IOException(string.Format("File already exists: {0}", symlinkfile));
            Alphaleonis.Win32.Filesystem.File.CreateSymbolicLink(target, PrefixWithUNC(symlinkfile), asDir ? Alphaleonis.Win32.Filesystem.SymbolicLinkTarget.Directory : Alphaleonis.Win32.Filesystem.SymbolicLinkTarget.File);

            //Sadly we do not get a notification if the creation fails :(
            System.IO.FileAttributes attr = 0;
            if ((!asDir && FileExists(symlinkfile)) || (asDir && DirectoryExists(symlinkfile)))
                try { attr = GetFileAttributes(symlinkfile); }
                catch { }

            if ((attr & System.IO.FileAttributes.ReparsePoint) == 0)
                throw new System.IO.IOException(string.Format("Unable to create symlink, check account permissions: {0}", symlinkfile));
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.Directory.EnumerateFileSystemEntries(path); }
                catch (System.IO.PathTooLongException) { }

            var r = Alphaleonis.Win32.Filesystem.Directory.GetFileSystemEntries(PrefixWithUNC(path));
            for (var i = 0; i < r.Length; i++)
                r[i] = StripUNCPrefix(r[i]);

            return r;
        }

        public string PathGetFileName(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.Path.GetFileName(path); }
                catch (System.IO.PathTooLongException) { }

            return StripUNCPrefix(Alphaleonis.Win32.Filesystem.Path.GetFileName(PrefixWithUNC(path)));
        }

        public string PathGetDirectoryName(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.Path.GetDirectoryName(path); }
                catch (System.IO.PathTooLongException) { }

            return StripUNCPrefix(Alphaleonis.Win32.Filesystem.Path.GetDirectoryName(PrefixWithUNC(path)));
        }

        public string PathGetExtension(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.Path.GetExtension(path); }
                catch (System.IO.PathTooLongException) { }
            
            return StripUNCPrefix(Alphaleonis.Win32.Filesystem.Path.GetExtension(PrefixWithUNC(path)));
        }
        
        public string PathChangeExtension(string path, string extension)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.Path.ChangeExtension(path, extension); }
                catch (System.IO.PathTooLongException) { }
            
            return StripUNCPrefix(Alphaleonis.Win32.Filesystem.Path.ChangeExtension(PrefixWithUNC(path), extension));
        }

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.Directory.SetLastWriteTimeUtc(path, time);
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.File.SetLastWriteTimeUtc(PrefixWithUNC(path), time);
        }

        public void DirectorySetCreationTimeUtc(string path, DateTime time)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.Directory.SetCreationTimeUtc(path, time);
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.File.SetCreationTimeUtc(PrefixWithUNC(path), time);
        }

        public DateTime DirectoryGetLastWriteTimeUtc(string path)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    return System.IO.Directory.GetLastWriteTimeUtc(path);
                }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.Directory.GetLastWriteTimeUtc(PrefixWithUNC(path));
        }

        public DateTime DirectoryGetCreationTimeUtc(string path)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    return System.IO.Directory.GetCreationTimeUtc(path);
                }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.Directory.GetCreationTimeUtc(PrefixWithUNC(path));
        }

        public void FileMove(string source, string target)
        {
            if (!IsPathTooLong(source) && !IsPathTooLong(target))
                try 
                { 
                    System.IO.File.Move(source, target);
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.File.Move(PrefixWithUNC(source), PrefixWithUNC(target));
        }

        public long FileLength(string path)
        {
            if (!IsPathTooLong(path))
                try { return new System.IO.FileInfo(path).Length; }
                catch (System.IO.PathTooLongException) { }

            return new Alphaleonis.Win32.Filesystem.FileInfo(PrefixWithUNC(path)).Length;
        }

        public void DirectoryDelete(string path, bool recursive)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.Directory.Delete(path, recursive);
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.Directory.Delete(PrefixWithUNC(path), recursive);
        }

        private class FileSystemAccess
        {
            public FileSystemRights Rights;
            public AccessControlType ControlType;
            public string SID;
            public bool Inherited;
            public InheritanceFlags Inheritance;
            public PropagationFlags Propagation;

            public FileSystemAccess()
            {
            }

            public FileSystemAccess(FileSystemAccessRule rule)
            {
                Rights = rule.FileSystemRights;
                ControlType = rule.AccessControlType;
                SID = rule.IdentityReference.Value;
                Inherited = rule.IsInherited;
                Inheritance = rule.InheritanceFlags;
                Propagation = rule.PropagationFlags;
            }

            public FileSystemAccessRule Create(System.Security.AccessControl.FileSystemSecurity owner)
            {
                return (FileSystemAccessRule)owner.AccessRuleFactory(
                    new System.Security.Principal.SecurityIdentifier(SID),
                    (int)Rights,
                    Inherited,
                    Inheritance,
                    Propagation,
                    ControlType);
            }
        }

        private string SerializeObject<T>(T o)
        {
            using(var tw = new System.IO.StringWriter())
            {
                Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings() { Culture = System.Globalization.CultureInfo.InvariantCulture }).Serialize(tw, o);
                tw.Flush();
                return tw.ToString();
            }
        }

        private T DeserializeObject<T>(string data)
        {
            using(var tr = new System.IO.StringReader(data))
                return (T)Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings() { Culture = System.Globalization.CultureInfo.InvariantCulture }).Deserialize(tr, typeof(T));

        }

        private System.Security.AccessControl.FileSystemSecurity GetAccessControlDir(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.Directory.GetAccessControl(path); }
            catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.Directory.GetAccessControl(PrefixWithUNC(path));
        }

        private System.Security.AccessControl.FileSystemSecurity GetAccessControlFile(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.File.GetAccessControl(path); }
            catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.GetAccessControl(PrefixWithUNC(path));
        }

        private void SetAccessControlFile(string path, FileSecurity rules)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.File.SetAccessControl(path, rules); 
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.File.SetAccessControl(PrefixWithUNC(path), rules, AccessControlSections.All);
        }

        private void SetAccessControlDir(string path, DirectorySecurity rules)
        {
            if (!IsPathTooLong(path))
                try 
                { 
                    System.IO.Directory.SetAccessControl(path, rules); 
                    return;
                }
                catch (System.IO.PathTooLongException) { }

            Alphaleonis.Win32.Filesystem.Directory.SetAccessControl(PrefixWithUNC(path), rules, AccessControlSections.All);
        }

        public Dictionary<string, string> GetMetadata(string path)
        {
            var isDirTarget = path.EndsWith(DIRSEP);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;
            var dict = new Dictionary<string, string>();

            System.Security.AccessControl.FileSystemSecurity rules;

            if (isDirTarget)
                rules = GetAccessControlDir(targetpath);
            else
                rules = GetAccessControlFile(targetpath);

            var objs = new List<FileSystemAccess>();
            foreach(var f in rules.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)))
                objs.Add(new FileSystemAccess((FileSystemAccessRule)f));

            dict["win-ext:accessrules"] = SerializeObject(objs);

            return dict;
        }
            
        public void SetMetadata(string path, Dictionary<string, string> data, bool restorePermissions)
        {
            var isDirTarget = path.EndsWith(DIRSEP);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

            System.Security.AccessControl.FileSystemSecurity rules;

            if (isDirTarget)
                rules = GetAccessControlDir(targetpath);                
            else
                rules = GetAccessControlFile(targetpath);

            if (restorePermissions && data.ContainsKey("win-ext:accessrules"))
            {
                var content = DeserializeObject<FileSystemAccess[]>(data["win-ext:accessrules"]);
                var c = rules.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier));
                for(var i = c.Count - 1; i >= 0; i--)
                    rules.RemoveAccessRule((System.Security.AccessControl.FileSystemAccessRule)c[i]);

                Exception ex = null;
               
                foreach (var r in content)
                {
                    // Attempt to apply as many rules as we can
                    try
                    {
                        rules.AddAccessRule((System.Security.AccessControl.FileSystemAccessRule)r.Create(rules));
                    }
                    catch(Exception e)
                    {
                        ex = e;
                    }
                }

                if (ex != null)
                    throw ex;

                if (isDirTarget)
                    SetAccessControlDir(targetpath, (DirectorySecurity)rules);
                else
                    SetAccessControlFile(targetpath, (FileSecurity)rules);
            }
        }

        #endregion    
    }
}

