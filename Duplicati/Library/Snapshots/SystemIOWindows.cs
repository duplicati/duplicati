//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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


namespace Duplicati.Library.Snapshots
{
    public struct SystemIOWindows : ISystemIO
    {
        private const string UNCPREFIX = @"\\?\";

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
                try { return System.IO.File.OpenRead(path); }
                catch (System.IO.PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.OpenRead(PrefixWithUNC(path));
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

        public string PathGetDirectoryName(string path)
        {
            if (!IsPathTooLong(path))
                try { return System.IO.Path.GetDirectoryName(path); }
                catch (System.IO.PathTooLongException) { }

            return StripUNCPrefix(Alphaleonis.Win32.Filesystem.Path.GetDirectoryName(PrefixWithUNC(path)));
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
        #endregion    
    }
}

