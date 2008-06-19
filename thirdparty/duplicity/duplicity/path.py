# Copyright 2002 Ben Escoto
#
# This file is part of duplicity.
#
# Duplicity is free software; you can redistribute it and/or modify it
# under the terms of the GNU General Public License as published by the
# Free Software Foundation; either version 3 of the License, or (at your
# option) any later version.
#
# Duplicity is distributed in the hope that it will be useful, but
# WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
# General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with duplicity; if not, write to the Free Software Foundation,
# Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

"""Wrapper class around a file like "/usr/bin/env"

This class makes certain file operations more convenient and
associates stat information with filenames

"""

import stat, os, errno, socket, time, re, gzip
import librsync, log, dup_time
from lazy import *

_copy_blocksize = 64 * 1024
_tmp_path_counter = 1

class StatResult:
	"""Used to emulate the output of os.stat() and related"""
	# st_mode is required by the TarInfo class, but it's unclear how
	# to generate it from file permissions.
	st_mode = 0


class PathException(Exception): pass

class ROPath:
	"""Read only Path

	Objects of this class doesn't represent real files, so they don't
	have a name.  They are required to be indexed though.

	"""
	def __init__(self, index, stat = None):
		"""ROPath initializer"""
		self.opened, self.fileobj = None, None
		self.index = index
		self.stat, self.type = None, None
		self.mode, self.devnums = None, None

	def set_from_stat(self):
		"""Set the value of self.type, self.mode from self.stat"""
		if not self.stat: self.type = None

		st_mode = self.stat.st_mode
		if stat.S_ISREG(st_mode): self.type = "reg"
		elif stat.S_ISDIR(st_mode): self.type = "dir"
		elif stat.S_ISLNK(st_mode): self.type = "sym"
		elif stat.S_ISFIFO(st_mode): self.type = "fifo"
		elif stat.S_ISSOCK(st_mode):
			raise PathException(self.get_relative_path() +
								"is a socket, unsupported by tar")
			self.type = "sock"
		elif stat.S_ISCHR(st_mode): self.type = "chr"
		elif stat.S_ISBLK(st_mode): self.type = "blk"
		else: raise PathException("Unknown type")

		self.mode = stat.S_IMODE(st_mode)
		# The following can be replaced with major(), minor() macros
		# in later versions of python (>= 2.3 I think)
		if self.type in ("chr", "blk"):
			self.devnums = (self.stat.st_rdev >> 8, self.stat.st_rdev & 0xff)
		
	def blank(self):
		"""Black out self - set type and stat to None"""
		self.type, self.stat = None, None

	def exists(self):
		"""True if corresponding file exists"""
		return self.type

	def isreg(self):
		"""True if self corresponds to regular file"""
		return self.type == "reg"

	def isdir(self):
		"""True if self is dir"""
		return self.type == "dir"

	def issym(self):
		"""True if self is sym"""
		return self.type == "sym"

	def isfifo(self):
		"""True if self is fifo"""
		return self.type == "fifo"

	def issock(self):
		"""True is self is socket"""
		return self.type == "sock"

	def isdev(self):
		"""True is self is a device file"""
		return self.type == "chr" or self.type == "blk"

	def getdevloc(self):
		"""Return device number path resides on"""
		return self.stat.st_dev

	def getsize(self):
		"""Return length in bytes from stat object"""
		return self.stat.st_size

	def getmtime(self):
		"""Return mod time of path in seconds"""
		return int(self.stat.st_mtime)

	def get_relative_path(self):
		"""Return relative path, created from index"""
		if self.index: return "/".join(self.index)
		else: return "."

	def getperms(self):
		"""Return permissions mode"""
		return self.mode

	def open(self, mode):
		"""Return fileobj associated with self"""
		assert mode == "rb" and self.fileobj and not self.opened, \
			   "%s %s %s" % (mode, self.fileobj, self.opened)
		self.opened = 1
		return self.fileobj

	def get_data(self):
		"""Return contents of associated fileobj in string"""
		fin = self.open("rb")
		buf = fin.read()
		assert not fin.close()
		return buf

	def setfileobj(self, fileobj):
		"""Set file object returned by open()"""
		assert not self.fileobj
		self.fileobj = fileobj
		self.opened = None

	def init_from_tarinfo(self, tarinfo):
		"""Set data from tarinfo object (part of tarfile module)"""
		# Set the typepp
		type = tarinfo.type
		if type == tarfile.REGTYPE or type == tarfile.AREGTYPE:
			self.type = "reg"
		elif type == tarfile.LNKTYPE:
			raise PathException("Hard links not supported yet")
		elif type == tarfile.SYMTYPE:
			self.type = "sym"
			self.symtext = tarinfo.linkname
		elif type == tarfile.CHRTYPE:
			self.type = "chr"
			self.devnums = (tarinfo.devmajor, tarinfo.devminor)
		elif type == tarfile.BLKTYPE:
			self.type = "blk"
			self.devnums = (tarinfo.devmajor, tarinfo.devminor)
		elif type == tarfile.DIRTYPE: self.type = "dir"
		elif type == tarfile.FIFOTYPE: self.type = "fifo"
		else: raise PathException("Unknown tarinfo type %s" % (type,))

		self.mode = tarinfo.mode
		self.stat = StatResult()

		# Set user and group id
		try: self.stat.st_uid = tarfile.uname2uid(tarinfo.uname)
		except KeyError: self.stat.st_uid = tarinfo.uid
		try: self.stat.st_gid = tarfile.gname2gid(tarinfo.gname)
		except KeyError: self.stat.st_gid = tarinfo.gid

		self.stat.st_mtime = int(tarinfo.mtime)
		self.stat.st_size = tarinfo.size

	def get_ropath(self):
		"""Return ropath copy of self"""
		new_ropath = ROPath(self.index, self.stat)
		new_ropath.type, new_ropath.mode = self.type, self.mode
		if self.issym(): new_ropath.symtext = self.symtext
		elif self.isdev(): new_ropath.devnums = self.devnums
		if self.exists(): new_ropath.stat = self.stat
		return new_ropath

	def get_tarinfo(self):
		"""Generate a tarfile.TarInfo object based on self

		Doesn't set size based on stat, because we may want to replace
		data wiht other stream.  Size should be set separately by
		calling function.

		"""
		ti = tarfile.TarInfo()
		if self.index: ti.name = "/".join(self.index)
		else: ti.name = "."
		if self.isdir(): ti.name += "/" # tar dir naming convention

		ti.size = 0
		if self.type:
			# Lots of this is specific to tarfile.py, hope it doesn't
			# change much...
			if self.isreg():
				ti.type = tarfile.REGTYPE
				ti.size = self.stat.st_size
			elif self.isdir(): ti.type = tarfile.DIRTYPE
			elif self.isfifo(): ti.type = tarfile.FIFOTYPE
			elif self.issym():
				ti.type = tarfile.SYMTYPE
				ti.linkname = self.symtext
			elif self.isdev():
				if self.type == "chr": ti.type = tarfile.CHRTYPE
				else: ti.type = tarfile.BLKTYPE
				ti.devmajor, ti.devminor = self.devnums
			else: raise PathError("Unrecognized type " + str(self.type))

			ti.mode = self.mode
			ti.uid, ti.gid = self.stat.st_uid, self.stat.st_gid
			if self.stat.st_mtime < 0:
				log.Warn("Warning: %s has negative mtime, treating as 0."
						 % (self.get_relative_path(),))
				ti.mtime = 0
			else: ti.mtime = int(self.stat.st_mtime)

			try: ti.uname = tarfile.uid2uname(ti.uid)
			except KeyError: pass
			try: ti.gname = tarfile.gid2gname(ti.gid)
			except KeyError: pass

			if ti.type in (tarfile.CHRTYPE, tarfile.BLKTYPE):
				if hasattr(os, "major") and hasattr(os, "minor"):
					ti.devmajor, ti.devminor = self.devnums
		else:
			# Currently we depend on an uninitiliazed tarinfo file to
			# already have appropriate headers.  Still, might as well
			# make sure mode and size set.
			ti.mode, ti.size = 0, 0
		return ti

	def __eq__(self, other):
		"""Used to compare two ROPaths.  Doesn't look at fileobjs"""
		if not self.type and not other.type: return 1 # neither exists
		if not self.stat and other.stat or not other.stat and self.stat:
			return 0
		if self.type != other.type: return 0

		if self.isreg() or self.isdir() or self.isfifo():
			# Don't compare sizes, because we might be comparing
			# signature size to size of file.
			if not self.perms_equal(other): return 0
			if int(self.stat.st_mtime) == int(other.stat.st_mtime): return 1
			# Below, treat negative mtimes as equal to 0
			return self.stat.st_mtime <= 0 and other.stat.st_mtime <= 0
		elif self.issym(): # here only symtext matters
			return self.symtext == other.symtext
		elif self.isdev():
			return self.perms_equal(other) and self.devnums == other.devnums
		assert 0

	def __ne__(self, other): return not self.__eq__(other)

	def compare_verbose(self, other, include_data = 0):
		"""Compare ROPaths like __eq__, but log reason if different

		This is placed in a separate function from __eq__ because
		__eq__ should be very time sensitive, and logging statements
		would slow it down.  Used when verifying.

		If include_data is true, also read all the data of regular
		files and see if they differ.

		"""
		def log_diff(log_string):
			log_str = "Difference found: " + log_string
			log.Log(log_str % (self.get_relative_path(),), 4)

		if not self.type and not other.type: return 1
		if not self.stat and other.stat:
			log_diff("New file %s")
			return 0
		if not other.stat and self.stat:
			log_diff("File %s is missing")
			return 0
		if self.type != other.type:
			log_diff("File %%s has type %s, expected %s" %
					 (other.type, self.type))
			return 0

		if self.isreg() or self.isdir() or self.isfifo():
			if not self.perms_equal(other):
				log_diff("File %%s has permissions %o, expected %o" %
						 (other.getperms(), self.getperms()))
				return 0
			if ((int(self.stat.st_mtime) != int(other.stat.st_mtime)) and
				(self.stat.st_mtime > 0 or other.stat.st_mtime > 0)):
				log_diff("File %%s has mtime %s, expected %s" %
						 (dup_time.timetopretty(int(other.stat.st_mtime)),
						  dup_time.timetopretty(int(self.stat.st_mtime))))
				return 0
			if self.isreg() and include_data:
				if self.compare_data(other): return 1
				else:
					log_diff("Data for file %s is different")
					return 0
			else: return 1
		elif self.issym():
			if self.symtext == other.symtext: return 1
			else:
				log_diff("Symlink %%s points to %s, expected %s" %
						 (other.symtext, self.symtext))
				return 0
		elif self.isdev():
			if not self.perms_equal(other):
				log_diff("File %%s has permissions %o, expected %o" %
						 (other.getperms(), self.getperms()))
				return 0
			if self.devnums != other.devnums:
				log_diff("Device file %%s has numbers %s, expected %s"
						 % (other.devnums, self.devnums))
				return 0
			return 1
		assert 0
		
	def compare_data(self, other):
		"""Compare data from two regular files, return true if same"""
		f1 = self.open("rb")
		f2 = other.open("rb")
		def close():
			assert not f1.close()
			assert not f2.close()
		while 1:
			buf1 = f1.read(_copy_blocksize)
			buf2 = f2.read(_copy_blocksize)
			if buf1 != buf2:
				close()
				return 0
			if not buf1:
				close()
				return 1

	def perms_equal(self, other):
		"""True if self and other have same permissions and ownership"""
		s1, s2 = self.stat, other.stat
		return (self.mode == other.mode and
				s1.st_gid == s2.st_gid and s1.st_uid == s2.st_uid)

	def copy(self, other):
		"""Copy self to other.  Also copies data.  Other must be Path"""
		if self.isreg(): other.writefileobj(self.open("rb"))
		elif self.isdir(): os.mkdir(other.name)
		elif self.issym():
			os.symlink(self.symtext, other.name)
			other.setdata()
			return # no need to copy symlink attributes
		elif self.isfifo(): os.mkfifo(other.name)
		elif self.issock(): socket.socket(socket.AF_UNIX).bind(other.name)
		elif self.isdev():
			if self.type == "chr": devtype = "c"
			else: devtype = "b"
			other.makedev(devtype, *self.devnums)
		self.copy_attribs(other)

	def copy_attribs(self, other):
		"""Only copy attributes from self to other"""
		if isinstance(other, Path):
			if hasattr(os, 'chown'):
				os.chown(other.name, self.stat.st_uid, self.stat.st_gid)
			if hasattr(os, 'chmod'):
				os.chmod(other.name, self.mode)
			if hasattr(os, 'utime'):
				os.utime(other.name, (time.time(), self.stat.st_mtime))
			other.setdata()
		else: # write results to fake stat object
			assert isinstance(other, ROPath)
			stat = StatResult()
			stat.st_uid, stat.st_gid = self.stat.st_uid, self.stat.st_gid
			stat.st_mtime = int(self.stat.st_mtime)
			other.stat = stat
			other.mode = self.mode

	def __repr__(self):
		"""Return string representation"""
		return "(%s %s)" % (self.index, self.type)


class Path(ROPath):
	"""Path class - wrapper around ordinary local files

	Besides caching stat() results, this class organizes various file
	code.

	"""
	regex_chars_to_quote = re.compile("[\\\\\\\"\\$`]")

	def __init__(self, base, index = ()):
		"""Path initializer"""
		# self.opened should be true if the file has been opened, and
		# self.fileobj can override returned fileobj 
		self.opened, self.fileobj = None, None
		self.base = base
		self.index = index
		self.name = os.path.join(base, *index)
		self.setdata()

	def setdata(self):
		"""Refresh stat cache"""
		try: self.stat = os.lstat(self.name)
		except OSError, e:
			err_string = errno.errorcode[e[0]]
			if err_string == "ENOENT" or err_string == "ENOTDIR":
				self.stat, self.type = None, None # file doesn't exist
				self.mode = None
			else: raise
		else:
			self.set_from_stat()
			if self.issym(): self.symtext = os.readlink(self.name)

	def append(self, ext):
		"""Return new Path with ext added to index"""
		return self.__class__(self.base, self.index + (ext,))

	def new_index(self, index):
		"""Return new Path with index index"""
		return self.__class__(self.base, index)

	def listdir(self):
		"""Return list generated by os.listdir"""
		return os.listdir(self.name)

	def isemptydir(self):
		"""Return true if path is a directory and is empty"""
		return self.isdir() and not self.listdir()

	def open(self, mode = "rb"):
		"""Return fileobj associated with self

		Usually this is just the file data on disk, but can be
		replaced with arbitrary data using the setfileobj method.

		"""
		assert not self.opened
		if self.fileobj: result = self.fileobj
		else: result = open(self.name, mode)
		return result

	def makedev(self, type, major, minor):
		"""Make a device file with specified type, major/minor nums"""
		cmdlist = ['mknod', self.name, type, str(major), str(minor)]
		if os.spawnvp(os.P_WAIT, 'mknod', cmdlist) != 0:
			raise PathException("Error running %s" % cmdlist)
		self.setdata()

	def mkdir(self):
		"""Make a directory at specified path"""
		log.Log("Making directory %s" % (self.name,), 7)
		try:
			os.mkdir(self.name)
		except OSError:
			if (not globals.force):
				raise PathException("Error creating directory %s" % (self.name,), 7)
		self.setdata()

	def delete(self):
		"""Remove this file"""
		log.Log("Deleting %s" % (self.name,), 7)
		if self.opened:
			self.close()
		
		if self.isdir(): os.rmdir(self.name)
		else: 
			try:
				os.unlink(self.name)
			except OSError:
				globals.badfiles.append(self.name)
				#print "Failed to erase file: " + str(self.name)
			
		self.setdata()

	def touch(self):
		"""Open the file, write 0 bytes, close"""
		log.Log("Touching %s" % (self.name,), 7)
		fp = self.open("wb")
		fp.close()

	def deltree(self):
		"""Remove self by recursively deleting files under it"""
		log.Log("Deleting tree %s" % (self.name,), 7)
		itr = IterTreeReducer(PathDeleter, [])
		for path in selection.Select(self).set_iter(): itr(path.index, path)
		itr.Finish()
		self.setdata()

	def get_parent_dir(self):
		"""Return directory that self is in"""
		if self.index: return Path(self.base, self.index[:-1])
		else:
			components = self.base.split("/")
			if len(components) == 2 and not components[0]:
				return Path("/") # already in root directory
			else: return Path("/".join(components[:-1]))

	def writefileobj(self, fin):
		"""Copy file object fin to self.  Close both when done."""
		fout = self.open("wb")
		while 1:
			buf = fin.read(_copy_blocksize)
			if not buf: break
			fout.write(buf)
		if fin.close() or fout.close():
			raise PathException("Error closing file object")
		self.setdata()

	def rename(self, new_path):
		"""Rename file at current path to new_path."""
		os.rename(self.name, new_path.name)
		self.setdata()
		new_path.setdata()

	def move(self, new_path):
		"""Like rename but destination may be on different file system"""
		self.copy(new_path)
		self.delete()

	def chmod(self, mode):
		"""Change permissions of the path"""
		os.chmod(self.name, mode)
		self.setdata()

	def patch_with_attribs(self, diff_ropath):
		"""Patch self with diff and then copy attributes over"""
		assert self.isreg() and diff_ropath.isreg()
		temp_path = self.get_temp_in_same_dir()
		patch_fileobj = librsync.PatchedFile(self.open("rb"),
											 diff_ropath.open("rb"))
		temp_path.writefileobj(patch_fileobj)
		diff_ropath.copy_attribs(temp_path)
		temp_path.rename(self)

	def get_temp_in_same_dir(self):
		"""Return temp non existent path in same directory as self"""
		global _tmp_path_counter
		parent_dir = self.get_parent_dir()
		while 1:
			temp_path = parent_dir.append("duplicity_temp." +
										  str(_tmp_path_counter))
			if not temp_path.type: return temp_path
			_tmp_path_counter += 1
			assert _tmp_path_counter < 10000, \
				   "Warning too many temp files created for " + self.name

	def compare_recursive(self, other, verbose = None):
		"""Compare self to other Path, descending down directories"""
		selfsel = selection.Select(self).set_iter()
		othersel = selection.Select(other).set_iter()
		return Iter.equal(selfsel, othersel, verbose)

	def __repr__(self):
		"""Return string representation"""
		return "(%s %s %s)" % (self.index, self.name, self.type)

	def quote(self, s = None):
		"""Return quoted version of s (defaults to self.name)

		The output is meant to be interpreted with shells, so can be
		used with os.system.

		"""
		if not s: s = self.name
		return '"%s"' % self.regex_chars_to_quote.sub(
			lambda m: "\\"+m.group(0), s)

	def unquote(self, s):
		"""Return unquoted version of string s, as quoted by above quote()"""
		assert s[0] == s[-1] == "\"" # string must be quoted by above
		result = ""; i = 1
		while i < len(s)-1:
			if s[i] == "\\":
				result += s[i+1]
				i += 2
			else:
				result += s[i]
				i += 1
		return result

	def get_filename(self):
		"""Return filename of last component"""
		components = self.name.split("/")
		assert components and components[-1]
		return components[-1]

	def get_canonical(self):
		"""Return string of canonical version of path

		Remove ".", and trailing slashes where possible.  Note that
		it's harder to remove "..", as "foo/bar/.." is not necessarily
		"foo", so we can't use path.normpath()

		"""
		newpath = "/".join(filter(lambda x: x and x != ".",
								  self.name.split("/")))
		if self.name[0] == "/": return "/" + newpath
		elif newpath: return newpath
		else: return "."


class DupPath(Path):
	"""Represent duplicity data files

	Based on the file name, files that are compressed or encrypted
	will have different open() methods.

	"""
	def __init__(self, base, index = (), parseresults = None):
		"""DupPath initializer

		The actual filename (no directory) must be the single element
		of the index, unless parseresults is given.

		"""
		if parseresults: self.pr = parseresults
		else:
			assert len(index) == 1
			self.pr = file_naming.parse(index[0])
			assert self.pr, "must be a recognizable duplicity file"

		Path.__init__(self, base, index)

	def filtered_open(self, mode = "rb", gpg_profile = None):
		"""Return fileobj with appropriate encryption/compression

		If encryption is specified but no gpg_profile, use
		globals.default_profile.

		"""
		assert not self.opened and not self.fileobj
		assert mode == "rb" or mode == "wb" # demand binary mode, no appends
		assert not (self.pr.encrypted and self.pr.compressed)
		if gpg_profile: assert self.pr.encrypted

		if self.pr.compressed: return gzip.GzipFile(self.name, mode)
		elif self.pr.encrypted: 
			if not gpg_profile: gpg_profile = globals.gpg_profile
			if mode == "rb": return gpg.GPGFile(None, self, gpg_profile)
			elif mode == "wb": return gpg.GPGFile(1, self, gpg_profile)
		else: return self.open(mode)


class PathDeleter(ITRBranch):
	"""Delete a directory.  Called by Path.deltree"""
	def start_process(self, index, path): self.path = path
	def end_process(self): self.path.delete()
	def can_fast_process(self, index, path): return not path.isdir()
	def fast_process(self, index, path): path.delete()

	
# Wait until end to avoid circular module trouble
import robust, tarfile, log, selection, globals, gpg, file_naming
