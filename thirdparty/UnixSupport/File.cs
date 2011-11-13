using System;
using Mono.Unix.Native;

namespace UnixSupport
{
	public static class File
	{
		/// <summary>
		/// Opens the file and honors advisory locking.
		/// </summary>
		/// <returns>A open stream that references the file</returns>
		/// <param name='path'>The full path to the file</param>
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
	}
}

