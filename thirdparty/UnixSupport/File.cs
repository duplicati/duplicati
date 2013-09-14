using System;
using System.Collections.Generic;
using Mono.Unix.Native;

namespace UnixSupport
{
	public static class File
	{
    
        private static readonly bool SUPPORTS_LLISTXATTR;
        
        static File ()
        {
            bool works = false;
            try
            { 
                string[] v;
                Mono.Unix.Native.Syscall.llistxattr("/", out v);
                works = true;
            }
            catch (EntryPointNotFoundException e)
            {
            }
            catch
            {
            }
            SUPPORTS_LLISTXATTR = works;
        }
        
		/// <summary>
		/// Opens the file and honors advisory locking.
		/// </summary>
		/// <returns>A open stream that references the file</returns>
		/// <param name="path">The full path to the file</param>
		public static System.IO.Stream OpenExclusive(string path, System.IO.FileAccess mode) 
		{
			return OpenExclusive(path, mode, (int)Mono.Unix.Native.FilePermissions.DEFFILEMODE);
		}

		/// <summary>
		/// Opens the file and honors advisory locking.
		/// </summary>
		/// <returns>A open stream that references the file</returns>
		/// <param name="path">The full path to the file</param>
		/// <param name="filemode">The file create mode</param>
		public static System.IO.Stream OpenExclusive(string path, System.IO.FileAccess mode, int filemode) 
		{
			Flock lck;
			lck.l_len = 0;
			lck.l_pid = Syscall.getpid();
			lck.l_start = 0;
			lck.l_type = LockType.F_WRLCK;
			lck.l_whence = SeekFlags.SEEK_SET;
						
			OpenFlags flags = OpenFlags.O_CREAT;
			if (mode == System.IO.FileAccess.Read) 
			{
				lck.l_type = LockType.F_RDLCK;
				flags |= OpenFlags.O_RDONLY;
			} else if (mode == System.IO.FileAccess.Write) {
				flags |= OpenFlags.O_WRONLY;
			} else {
				flags |= OpenFlags.O_RDWR;
			}
			
			int fd = Syscall.open(path, flags, (Mono.Unix.Native.FilePermissions)filemode);
			if (fd > 0) 
			{
				//This does not work on OSX, it gives ENOTTY
				//int res = Syscall.fcntl(fd, Mono.Unix.Native.FcntlCommand.F_SETLK, ref lck);
				
				//This is the same (at least for our purpose, and works on OSX)
				int res = Syscall.lockf(fd, LockfCommand.F_TLOCK, 0);

				//If we have the lock, return the stream
				if (res == 0)
					return new Mono.Unix.UnixStream(fd);
				else 
				{
					Mono.Unix.Native.Syscall.close(fd);
					throw new LockedFileException(path, mode);
				}
			}
			
			throw new BadFileException(path);
		}

		[Serializable]
		private class BadFileException : System.IO.IOException
		{
			public BadFileException(string filename)
				: base(string.Format("Unable to open the file \"{0}\", error: {1} ({2})", filename, Syscall.GetLastError(), (int)Syscall.GetLastError()))
			{
			}
		}

		[Serializable]
		private class LockedFileException : System.IO.IOException
		{
			public LockedFileException(string filename, System.IO.FileAccess mode)
				: base(string.Format("Unable to open the file \"{0}\" in mode {1}, error: {2} ({3})", filename, mode, Syscall.GetLastError(), (int)Syscall.GetLastError()))
			{
			}
		}

        [Serializable]
        private class FileAccesException : System.IO.IOException
        {
            public FileAccesException(string filename, string method)
                : base(string.Format("Unable to access the file \"{0}\" with method {1}, error: {2} ({3})", filename, method, Syscall.GetLastError(), (int)Syscall.GetLastError()))
            {
            }
        }
        
        /// <summary>
        /// Gets the symlink target for the given path
        /// </summary>
        /// <param name="path">The path to get the symlink target for</param>
        /// <returns>The symlink target</returns>
        public static string GetSymlinkTarget(string path)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(2048); //2kb, should cover utf16 * 1023 chars
            if (Mono.Unix.Native.Syscall.readlink(path, sb, (ulong)sb.Capacity) >= 0)
                return sb.ToString();

            throw new System.IO.FileLoadException(string.Format("Unable to get symlink for \"{0}\", error: {1} ({2})", path, Syscall.GetLastError(), (int)Syscall.GetLastError()));
        }

        /// <summary>
        /// Creates a new symlink
        /// </summary>
        /// <param name="path">The path to create the symbolic link entry</param>
        /// <param name="target">The path the symbolic link points to</param>
        public static void CreateSymlink(string path, string target)
        {
            if (Mono.Unix.Native.Syscall.symlink(target, path) != 0)
                throw new System.IO.IOException(string.Format("Unable to create symlink from \"{0}\" to \"{1}\", error: {2} ({3})", path, target, Syscall.GetLastError(), (int)Syscall.GetLastError()));
        }
        
        /// <summary>
        /// Enum that describes the different filesystem entry types
        /// </summary>
        public enum FileType
        {
            File,
            Directory,
            Symlink,
            Fifo,
            Socket,
            CharacterDevice,
            BlockDevice,
            Unknown
        }
        
        /// <summary>
        /// Gets the type of the file.
        /// </summary>
        /// <returns>The file type</returns>
        /// <param name="path">The full path to look up</param>
        public static FileType GetFileType(string path)
        {
            
            var fse = Mono.Unix.UnixFileInfo.GetFileSystemEntry(path);
            if (fse.IsRegularFile)
                return FileType.File;
            else if (fse.IsDirectory)
                return FileType.Directory;
            else if (fse.IsSymbolicLink)
                return FileType.Symlink;
            else if (fse.IsFifo)
                return FileType.Fifo;
            else if (fse.IsSocket)
                return FileType.Socket;
            else if (fse.IsCharacterDevice)
                return FileType.CharacterDevice;
            else if (fse.IsBlockDevice)
                return FileType.CharacterDevice;
            else
                return FileType.Unknown;
        }
        
        
        /// <summary>
        /// Gets the extended attributes.
        /// </summary>
        /// <returns>The extended attributes.</returns>
        /// <param name="path">The full path to look up</param>
        public static Dictionary<string, byte[]> GetExtendedAttributes(string path)
        {
            string[] values;
            var size = SUPPORTS_LLISTXATTR ? Mono.Unix.Native.Syscall.llistxattr(path, out values) : Mono.Unix.Native.Syscall.listxattr(path, out values);
            if (size < 0)
                throw new FileAccesException(path, "llistxattr");
            
            var dict = new Dictionary<string, byte[]>();
            foreach(var s in values)
            {
                byte[] v;
                var n = SUPPORTS_LLISTXATTR ? Mono.Unix.Native.Syscall.lgetxattr(path, s, out v) : Mono.Unix.Native.Syscall.getxattr(path, s, out v);
                if (n > 0)
                    dict.Add(s, v);
            }
            
            return dict;
        }
        
        /// <summary>
        /// Describes the basic user/group/perm tuplet for a file or folder
        /// </summary>
        public struct FileInfo
        {
            public readonly long UID;
            public readonly long GID;
            public readonly long Permissions;
            
            internal FileInfo(Mono.Unix.UnixFileSystemInfo fse)
            {
                UID = fse.OwnerUser.UserId;
                GID = fse.OwnerGroupId;
                Permissions = (long)fse.FileAccessPermissions;
            }
        }
        
        /// <summary>
        /// Gets the basic user/group/perm tuplet for a file or folder
        /// </summary>
        /// <returns>The basic user/group/perm tuplet for a file or folder</returns>
        /// <param name="path">The full path to look up</param>
        public static FileInfo GetUserGroupAndPermissions(string path)
        {
            return new FileInfo(Mono.Unix.UnixFileInfo.GetFileSystemEntry(path));
        }
	}
}

