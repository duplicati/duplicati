using System;

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
			Mono.Unix.Native.Flock lck;
			lck.l_len = 0;
			lck.l_pid = Mono.Unix.Native.Syscall.getpid();
			lck.l_start = 0;
			lck.l_type = Mono.Unix.Native.LockType.F_WRLCK;
			lck.l_whence = Mono.Unix.Native.SeekFlags.SEEK_SET;
			
			Mono.Unix.Native.OpenFlags flags = Mono.Unix.Native.OpenFlags.O_CREAT;
			if (mode == System.IO.FileAccess.Read) 
			{
				lck.l_type = Mono.Unix.Native.LockType.F_RDLCK;
				flags |= Mono.Unix.Native.OpenFlags.O_RDONLY;
			} else if (mode == System.IO.FileAccess.Write) {
				flags |= Mono.Unix.Native.OpenFlags.O_WRONLY;
			} else {
				flags |= Mono.Unix.Native.OpenFlags.O_RDWR;
			}
			
			int fd = Mono.Unix.Native.Syscall.open(path, flags, (Mono.Unix.Native.FilePermissions)filemode);
			if (fd > 0) 
			{
				int res = Mono.Unix.Native.Syscall.fcntl(fd, Mono.Unix.Native.FcntlCommand.F_SETLK, ref lck);

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
				: base(string.Format("Unable to open the file \"{0}\", error: {1} ({2})", filename, Mono.Unix.Native.Syscall.GetLastError(), (int)Mono.Unix.Native.Syscall.GetLastError()))
			{
			}
		}

		[Serializable]
		private class LockedFileException : System.IO.IOException
		{
			public LockedFileException(string filename, System.IO.FileAccess mode)
				: base(string.Format("Unable to open the file \"{0}\" in mode {1}", filename, mode))
			{
			}
		}
	}
}

