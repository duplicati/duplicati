#!/usr/bin/env python
#-------------------------------------------------------------------
# tarfile.py
#
# Module for reading and writing .tar and tar.gz files.
#
# Needs at least Python version 2.2.
#
# Please consult the html documentation in this distribution
# for further details on how to use tarfile.
#
#-------------------------------------------------------------------
# Copyright (C) 2002 Lars Gustabel <lars@gustaebel.de>
# All rights reserved.
#
# Permission  is  hereby granted,  free  of charge,  to  any person
# obtaining a  copy of  this software  and associated documentation
# files  (the  "Software"),  to   deal  in  the  Software   without
# restriction,  including  without limitation  the  rights to  use,
# copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies  of  the  Software,  and to  permit  persons  to  whom the
# Software  is  furnished  to  do  so,  subject  to  the  following
# conditions:
#
# The above copyright  notice and this  permission notice shall  be
# included in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS  IS", WITHOUT WARRANTY OF ANY  KIND,
# EXPRESS OR IMPLIED, INCLUDING  BUT NOT LIMITED TO  THE WARRANTIES
# OF  MERCHANTABILITY,  FITNESS   FOR  A  PARTICULAR   PURPOSE  AND
# NONINFRINGEMENT.  IN  NO  EVENT SHALL  THE  AUTHORS  OR COPYRIGHT
# HOLDERS  BE LIABLE  FOR ANY  CLAIM, DAMAGES  OR OTHER  LIABILITY,
# WHETHER  IN AN  ACTION OF  CONTRACT, TORT  OR OTHERWISE,  ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
# OTHER DEALINGS IN THE SOFTWARE.
#
"""Read from and write to tar format archives.
"""

__version__ = "$Revision: 1.6 $"
# $Source: /sources/duplicity/duplicity/duplicity/tarfile.py,v $

version     = "0.4.9"
__author__  = "Lars Gustabel (lars@gustaebel.de)"
__date__    = "$Date: 2007/05/30 00:30:57 $"
__cvsid__   = "$Id: tarfile.py,v 1.6 2007/05/30 00:30:57 loafman Exp $"
__credits__ = "Gustavo Niemeyer for his support, " \
              "Detlef Lannert for some early contributions"

#---------
# Imports
#---------
import sys
import os
import __builtin__
import shutil
import stat
import errno
import time

try:
    import grp, pwd
except ImportError:
    grp = pwd = None
# These are used later to cache user and group names and ids
gname_dict = uname_dict = uid_dict = gid_dict = None

# We won't need this anymore in Python 2.3
#
# We import the _tarfile extension, that contains
# some useful functions to handle devices and symlinks.
# We inject them into os module, as if we were under 2.3.
#
try:
    import _tarfile
    if _tarfile.mknod is None:
        _tarfile = None
except ImportError:
    _tarfile = None
if _tarfile and not hasattr(os, "mknod"):
    os.mknod = _tarfile.mknod
if _tarfile and not hasattr(os, "major"):
    os.major = _tarfile.major
if _tarfile and not hasattr(os, "minor"):
    os.minor = _tarfile.minor
if _tarfile and not hasattr(os, "makedev"):
    os.makedev = _tarfile.makedev
if _tarfile and not hasattr(os, "lchown"):
    os.lchown = _tarfile.lchown

# XXX remove for release (2.3)
if sys.version_info[:2] < (2,3):
    True  = 1
    False = 0

#---------------------------------------------------------
# GNUtar constants
#---------------------------------------------------------
BLOCKSIZE  = 512                # length of processing blocks
RECORDSIZE = BLOCKSIZE * 20     # length of records
MAGIC      = "ustar"            # magic tar string
VERSION    = "00"               # version number

LENGTH_NAME = 100               # maximal length of a filename
LENGTH_LINK = 100               # maximal length of a linkname

REGTYPE  = "0"                  # regular file
AREGTYPE = "\0"                 # regular file
LNKTYPE  = "1"                  # link (inside tarfile)
SYMTYPE  = "2"                  # symbolic link
CHRTYPE  = "3"                  # character special device
BLKTYPE  = "4"                  # block special device
DIRTYPE  = "5"                  # directory
FIFOTYPE = "6"                  # fifo special device
CONTTYPE = "7"                  # contiguous file

GNUTYPE_LONGNAME = "L"          # GNU tar extension for longnames
GNUTYPE_LONGLINK = "K"          # GNU tar extension for longlink
GNUTYPE_SPARSE   = "S"          # GNU tar extension for sparse file

#---------------------------------------------------------
# tarfile constants
#---------------------------------------------------------
SUPPORTED_TYPES = (REGTYPE, AREGTYPE, LNKTYPE,  # file types that tarfile
                   SYMTYPE, DIRTYPE, FIFOTYPE,  # can cope with.
                   CONTTYPE, GNUTYPE_LONGNAME,
                   GNUTYPE_LONGLINK, GNUTYPE_SPARSE,
                   CHRTYPE, BLKTYPE)

REGULAR_TYPES = (REGTYPE, AREGTYPE,             # file types that somehow
                 CONTTYPE, GNUTYPE_SPARSE)      # represent regular files

#---------------------------------------------------------
# Bits used in the mode field, values in octal.
#---------------------------------------------------------
S_IFLNK = 0120000        # symbolic link
S_IFREG = 0100000        # regular file
S_IFBLK = 0060000        # block device
S_IFDIR = 0040000        # directory
S_IFCHR = 0020000        # character device
S_IFIFO = 0010000        # fifo

TSUID   = 04000          # set UID on execution
TSGID   = 02000          # set GID on execution
TSVTX   = 01000          # reserved

TUREAD  = 00400          # read by owner
TUWRITE = 00200          # write by owner
TUEXEC  = 00100          # execute/search by owner
TGREAD  = 00040          # read by group
TGWRITE = 00020          # write by group
TGEXEC  = 00010          # execute/search by group
TOREAD  = 00004          # read by other
TOWRITE = 00002          # write by other
TOEXEC  = 00001          # execute/search by other

#---------------------------------------------------------
# Some useful functions
#---------------------------------------------------------
def nts(s):
    """Convert a null-terminated string buffer to a python string.
    """
    return s.split("\0", 1)[0]

def calc_chksum(buf):
    """Calculate the checksum for a member's header. It's a simple addition
       of all bytes, treating the chksum field as if filled with spaces.
       buf is a 512 byte long string buffer which holds the header.
    """
    chk = 256                           # chksum field is treated as blanks,
                                        # so the initial value is 8 * ord(" ")
    for c in buf[:148]: chk += ord(c)   # sum up all bytes before chksum
    for c in buf[156:]: chk += ord(c)   # sum up all bytes after chksum
    return chk

def copyfileobj(src, dst, length=None):
    """Copy length bytes from fileobj src to fileobj dst.
       If length is None, copy the entire content.
    """
    if length == 0:
        return
    if length is None:
        shutil.copyfileobj(src, dst)
        return

    BUFSIZE = 16 * 1024
    blocks, remainder = divmod(length, BUFSIZE)
    for b in range(blocks):
        buf = src.read(BUFSIZE)
        if len(buf) < BUFSIZE:
            raise IOError, "end of file reached"
        dst.write(buf)

    if remainder != 0:
        buf = src.read(remainder)
        if len(buf) < remainder:
            raise IOError, "end of file reached"
        dst.write(buf)
    return

filemode_table = (
    (S_IFLNK, "l",
     S_IFREG, "-",
     S_IFBLK, "b",
     S_IFDIR, "d",
     S_IFCHR, "c",
     S_IFIFO, "p"),
    (TUREAD,  "r"),
    (TUWRITE, "w"),
    (TUEXEC,  "x", TSUID, "S", TUEXEC|TSUID, "s"),
    (TGREAD,  "r"),
    (TGWRITE, "w"),
    (TGEXEC,  "x", TSGID, "S", TGEXEC|TSGID, "s"),
    (TOREAD,  "r"),
    (TOWRITE, "w"),
    (TOEXEC,  "x", TSVTX, "T", TOEXEC|TSVTX, "t"))

def filemode(mode):
    """Convert a file's mode to a string of the form
       -rwxrwxrwx.
       Used by TarFile.list()
    """
    s = ""
    for t in filemode_table:
        while 1:
            if mode & t[0] == t[0]:
                s += t[1]
            elif len(t) > 2:
                t = t[2:]
                continue
            else:
                s += "-"
            break
    return s

if os.sep != "/":
    normpath = lambda path: os.path.normpath(path).replace(os.sep, "/")
else:
    normpath = os.path.normpath

class TarError(Exception):
    """Internally used exception"""
    pass

#--------------------
# exported functions
#--------------------
def open(name, mode="r", fileobj=None):
    """Open (uncompressed) tar archive name for reading, writing
       or appending.
    """
    return TarFile(name, mode, fileobj)

def gzopen(gzname, gzmode="r", compresslevel=9, fileobj=None):
    """Open gzip compressed tar archive name for reading or writing.
       Appending is not allowed.
    """
    if gzmode == "a":
        raise ValueError, "Appending to gzipped archive is not allowed"
    import gzip
    pre, ext = os.path.splitext(gzname)
    pre = os.path.basename(pre)
    if ext == ".tgz":
        ext = ".tar"
    if ext == ".gz":
        ext = ""
    tarname = pre + ext
    mode = gzmode
    if "b" not in gzmode:
        gzmode += "b"
    if mode[0:1] == "w":
        if not fileobj:
            fileobj = __builtin__.file(gzname, gzmode)
        t = TarFile(tarname, mode, gzip.GzipFile(tarname, gzmode,
                                                 compresslevel, fileobj))
    else:
        t = TarFile(tarname, mode, gzip.open(gzname, gzmode, compresslevel))
    t._extfileobj = 0
    return t

def is_tarfile(name):
    """Return True if name points to a tar archive that we
       are able to handle, else return False.
    """

    buftoinfo = TarFile.__dict__["_buftoinfo"]
    try:
        buf = __builtin__.open(name, "rb").read(BLOCKSIZE)
        buftoinfo(None, buf)
        return True
    except (ValueError, ImportError):
        pass
    try:
        import gzip
        buf = gzip.open(name, "rb").read(BLOCKSIZE)
        buftoinfo(None, buf)
        return True
    except (IOError, ValueError, ImportError):
        pass
    return False

#------------------
# Exported Classes
#------------------
class TarInfo:
    """Informational class which holds the details about an
       archive member given by a tar header block.
       TarInfo instances are returned by TarFile.getmember() and
       TarFile.getmembers() and are usually created internally.
       If you want to create a TarInfo instance from the outside,
       you should use TarFile.gettarinfo() if the file already exists,
       or you can instanciate the class yourself.
    """

    def __init__(self, name=""):
        """Construct a TarInfo instance. name is the optional name
           of the member.
        """

        self.name     = name       # member name (dirnames must end with '/')
        self.mode     = 0100666    # file permissions
        self.uid      = 0          # user id
        self.gid      = 0          # group id
        self.size     = 0          # file size
        self.mtime    = 0          # modification time
        self.chksum   = 0          # header checksum
        self.type     = REGTYPE    # member type
        self.linkname = ""         # link name
        self.uname    = "user"     # user name
        self.gname    = "group"    # group name
        self.devmajor = 0          #-
        self.devminor = 0          #-for use with CHRTYPE and BLKTYPE
        self.prefix   = ""         # prefix, holding information
                                   # about sparse files

        self.offset   = 0          # the tar header starts here
        self.offset_data = 0       # the optional file's data starts here

    def init_from_stat(self, statres):
        """Initialize various attributes from statobj (these are
        returned by os.stat() and related functions.  Return none on error"""
        stmd = statres.st_mode
        if stat.S_ISREG(stmd): type = REGTYPE
        elif stat.S_ISDIR(stmd):
            type = DIRTYPE
            if self.name[-1:] != "/": self.name += "/"
        elif stat.S_ISFIFO(stmd): type = FIFOTYPE
        elif stat.S_ISLNK(stmd): type = SYMTYPE
        elif stat.S_ISCHR(stmd): type = CHRTYPE
        elif stat.S_ISBLK(stmd): type = BLKTYPE
        else: return None

        # Fill the TarInfo instance with all
        # information we can get.
        self.mode  = stat.S_IMODE(stmd)
        self.uid   = statres.st_uid
        self.gid   = statres.st_gid
        self.size  = statres.st_size
        self.mtime = statres.st_mtime
        self.type  = type
        if pwd:
            try: self.uname = uid2uname(self.uid)
            except KeyError: pass
        if grp:
            try: self.gname = gid2gname(self.gid)
            except KeyError: pass

        if type in (CHRTYPE, BLKTYPE):
            if hasattr(os, "major") and hasattr(os, "minor"):
                self.devmajor = os.major(statres.st_rdev)
                self.devminor = os.minor(statres.st_rdev)
        return 1

    def set_arcname(self, name):
        """Set the name of the member in the archive.  Backward
        slashes are converted to forward slashes, Absolute paths are
        turned to relative paths.
        """
        arcname = normpath(name)
        drv, arcname = os.path.splitdrive(arcname)
        while arcname[0:1] == "/":
            arcname = arcname[1:]
        self.name = arcname

    def getheader(self):
        """Return a tar header block as a 512 byte string.
        """
        if self.uid > 2097151 or self.uid < 0:
            sys.stderr.write("uid %i of file %s not in range. Setting uid to 60001\n" % (self.uid,self.name))
            self.uid = 60001
        if self.gid > 2097151 or self.gid < 0:
            sys.stderr.write("gid %i of file %s not in range. Setting gid to 60001\n" % (self.gid, self.name))
            self.gid = 60001
        # The following code was contributed by Detlef Lannert.
        parts = []
        for value, fieldsize in (
                (self.name, 100),
                ("%07o" % self.mode, 8),
                ("%07o" % self.uid, 8),
                ("%07o" % self.gid, 8),
                ("%011o" % self.size, 12),
                ("%011o" % self.mtime, 12),
                ("        ", 8),
                (self.type, 1),
                (self.linkname, 100),
                (MAGIC, 6),
                (VERSION, 2),
                (self.uname, 32),
                (self.gname, 32),
                ("%07o" % self.devmajor, 8),
                ("%07o" % self.devminor, 8),
                (self.prefix, 155)
                ):
            l = len(value)
            parts.append(value + (fieldsize - l) * "\0")

        buf = "".join(parts)
        chksum = calc_chksum(buf)
        buf = buf[:148] + "%06o\0" % chksum + buf[155:]
        buf += (512 - len(buf)) * "\0"
        self.buf = buf
        return buf

    def isreg(self):
        return self.type in REGULAR_TYPES
    def isfile(self):
        return self.isreg()
    def isdir(self):
        return self.type == DIRTYPE
    def issym(self):
        return self.type == SYMTYPE
    def islnk(self):
        return self.type == LNKTYPE
    def ischr(self):
        return self.type == CHRTYPE
    def isblk(self):
        return self.type == BLKTYPE
    def isfifo(self):
        return self.type == FIFOTYPE
    def issparse(self):
        return self.type == GNUTYPE_SPARSE
    def isdev(self):
        return self.type in (CHRTYPE, BLKTYPE, FIFOTYPE)
# class TarInfo


class TarFile:
    """Class representing a TAR archive file on disk.
    """
    debug = 0                   # May be set from 0 (no msgs) to 3 (all msgs)

    dereference = False         # If true, add content of linked file to the
                                # tar file, else the link.

    ignore_zeros = False        # If true, skips empty or invalid blocks and
                                # continues processing.

    errorlevel = 0              # If 0, fatal errors only appear in debug
                                # messages (if debug >= 0). If > 0, errors
                                # are passed to the caller as exceptions.

    def __init__(self, name=None, mode="r", fileobj=None):
        self.name = name

        if len(mode) > 1 or mode not in "raw":
            raise ValueError, "mode must be either 'r', 'a' or 'w', " \
                                "not '%s'" % mode
        self._mode = mode
        self.mode = {"r": "rb", "a": "r+b", "w": "wb"}[mode]

        if not fileobj:
            fileobj = __builtin__.file(self.name, self.mode)
            self._extfileobj = 0
        else:
            if self.name is None and hasattr(fileobj, "name"):
                self.name = fileobj.name
            if hasattr(fileobj, "mode"):
                self.mode = fileobj.mode
            self._extfileobj = 1
        self.fileobj = fileobj

        self.init_datastructures()

        if self._mode == "a":
            self.fileobj.seek(0)
            self._load()

    def init_datastructures(self):
        # Init datastructures
        #self.members     = []       # list of members as TarInfo instances
        #self.membernames = []       # names of members
        #self.chunks      = [0]      # chunk cache
        self._loaded     = 0        # flag if all members have been read
        self.offset      = 0l       # current position in the archive file
        self.inodes      = {}       # dictionary caching the inodes of
                                    # archive members already added
        self.next_chunk = 0 # offset of next tarinfo, used when reading

    def close(self):
        """Close the TarFile instance and do some cleanup.
        """
        if self.fileobj:
            if self._mode in "aw":
                # fill up the end with zero-blocks
                # (like option -b20 for tar does)
                blocks, remainder = divmod(self.offset, RECORDSIZE)
                if remainder > 0:
                    self.fileobj.write("\0" * (RECORDSIZE - remainder))

            if not self._extfileobj:
                self.fileobj.close()
            self.fileobj = None

    def throwaway_until(self, position):
        """Read data, throwing it away until we get to position"""
        bufsize = 16 * 1024
        bytes_to_read = position - self.offset
        assert bytes_to_read >= 0
        while bytes_to_read >= bufsize:
            self.fileobj.read(bufsize)
            bytes_to_read -= bufsize
        self.fileobj.read(bytes_to_read)
        self.offset = position

    def next(self):
        """Return the next member from the archive.
           Return None if the end is reached.
           Can be used in a while statement, is used
           for Iteration (see __iter__()) and internally.
        """
        if not self.fileobj:
            raise ValueError, "I/O operation on closed file"
        if self._mode not in "ra":
            raise ValueError, "reading from a write-mode file"

        # Read the next block.
        # self.fileobj.seek(self.chunks[-1])
        #self.fileobj.seek(self.next_chunk)
        #self.offset = self.next_chunk
        self.throwaway_until(self.next_chunk)
        while 1:
            buf = self.fileobj.read(BLOCKSIZE)
            if not buf:
                return None
            try:
                tarinfo = self._buftoinfo(buf)
            except ValueError:
                if self.ignore_zeros:
                    if buf.count("\0") == BLOCKSIZE:
                        adj = "empty"
                    else:
                        adj = "invalid"
                    self._dbg(2, "0x%X: %s block\n" % (self.offset, adj))
                    self.offset += BLOCKSIZE
                    continue
                else:
                    return None
            break

        # If the TarInfo instance contains a GNUTYPE longname or longlink
        # statement, we must process this first.
        if tarinfo.type in (GNUTYPE_LONGLINK, GNUTYPE_LONGNAME):
            tarinfo = self._proc_gnulong(tarinfo, tarinfo.type)

        if tarinfo.issparse():
            assert 0, "Sparse file support turned off"
            # Sparse files need some care,
            # due to the possible extra headers.
            tarinfo.offset = self.offset
            self.offset += BLOCKSIZE
            origsize = self._proc_sparse(tarinfo)
            tarinfo.offset_data = self.offset
            blocks, remainder = divmod(tarinfo.size, BLOCKSIZE)
            if remainder:
                blocks += 1
            self.offset += blocks * BLOCKSIZE
            tarinfo.size = origsize
        else:
            tarinfo.offset = self.offset
            self.offset += BLOCKSIZE
            tarinfo.offset_data = self.offset
            if tarinfo.isreg():
                ## Skip the following data blocks.
                blocks, remainder = divmod(tarinfo.size, BLOCKSIZE)
                if remainder:
                    blocks += 1
                self.next_chunk = self.offset + (blocks * BLOCKSIZE)
            else: self.next_chunk = self.offset

        #self.members.append(tarinfo)  These use too much memory
        #self.membernames.append(tarinfo.name)
        #self.chunks.append(self.offset)
        return tarinfo

    def getmember(self, name):
        """Return a TarInfo instance for member name.
        """
        if name not in self.membernames and not self._loaded:
            self._load()
        if name not in self.membernames:
            raise KeyError, "filename `%s' not found in tar archive" % name
        return self._getmember(name)

    def getinfo(self, name):
        """Return a TarInfo instance for member name.
           This method will be deprecated in 0.6,
           use getmember() instead.
        """
        # XXX kick this out in 0.6
        import warnings
        warnings.warn("use getmember() instead", DeprecationWarning)
        return self.getmember(name)

    def getmembers(self):
        """Return a list of all members in the archive
           (as TarInfo instances).
        """
        if not self._loaded:    # if we want to obtain a list of
            self._load()        # all members, we first have to
                                # scan the whole archive.
        return self.members

    def getnames(self):
        """Return a list of names of all members in the
           archive.
        """
        if not self._loaded:
            self._load()
        return self.membernames

    def gettarinfo(self, name, arcname=None):
        """Create a TarInfo instance from an existing file.
           Optional arcname defines the name under which the file
           shall be stored in the archive.
        """
        # Now, fill the TarInfo instance with
        # information specific for the file.
        tarinfo = TarInfo()

        if arcname is None: tarinfo.set_arcname(name)
        else: tarinfo.set_arcname(arcname)

        # Use os.stat or os.lstat, depending on platform
        # and if symlinks shall be resolved.
        if hasattr(os, "lstat") and not self.dereference:
            statres = os.lstat(name)
        else:
            statres = os.stat(name)

        if not tarinfo.init_from_stat(statres): return None

        if tarinfo.type == REGTYPE:
            inode = (statres.st_ino, statres.st_dev, statres.st_mtime)
            if inode in self.inodes.keys() and not self.dereference:
                # Is it a hardlink to an already
                # archived file?
                tarinfo.type = LNKTYPE
                tarinfo.linkname = self.inodes[inode]
            else:
                # The inode is added only if its valid.
                # For win32 it is always 0.
                if inode[0]: self.inodes[inode] = tarinfo.name
        elif tarinfo.type == SYMTYPE:
            tarinfo.linkname = os.readlink(name)
            tarinfo.size = 0

        return tarinfo

    def list(self, verbose=1):
        """Print a formatted listing of the archive's
           contents to stdout.
        """
        for tarinfo in self:
            if verbose:
                print filemode(tarinfo.mode),
                print tarinfo.uname + "/" + tarinfo.gname,
                if tarinfo.ischr() or tarinfo.isblk():
                    print "%10s" % (str(tarinfo.devmajor) + "," + str(tarinfo.devminor)),
                else:
                    print "%10d" % tarinfo.size,
                print "%d-%02d-%02d %02d:%02d:%02d" \
                      % time.gmtime(tarinfo.mtime)[:6],

            print tarinfo.name,

            if verbose:
                if tarinfo.issym():
                    print "->", tarinfo.linkname,
                if tarinfo.islnk():
                    print "link to", tarinfo.linkname,
            print

    def add(self, name, arcname=None, recursive=1):
        """Add a file or a directory to the archive.
           Directory addition is recursive by default.
        """
        if not self.fileobj:
            raise ValueError, "I/O operation on closed file"
        if self._mode == "r":
            raise ValueError, "writing to a read-mode file"

        if arcname is None:
            arcname = name

        # Skip if somebody tries to archive the archive...
        if os.path.abspath(name) == os.path.abspath(self.name):
            self._dbg(2, "tarfile: Skipped `%s'\n" % name)
            return

        # Special case: The user wants to add the current
        # working directory.
        if name == ".":
            if recursive:
                if arcname == ".":
                    arcname = ""
                for f in os.listdir("."):
                    self.add(f, os.path.join(arcname, f))
            return

        self._dbg(1, "%s\n" % name)

        # Create a TarInfo instance from the file.
        tarinfo = self.gettarinfo(name, arcname)

        if tarinfo is None:
            self._dbg(1, "tarfile: Unsupported type `%s'\n" % name)


        # Append the tar header and data to the archive.
        if tarinfo.isreg():
            f = __builtin__.file(name, "rb")
            self.addfile(tarinfo, fileobj = f)
            f.close()

        if tarinfo.type in (LNKTYPE, SYMTYPE, FIFOTYPE, CHRTYPE, BLKTYPE):
            tarinfo.size = 0l
            self.addfile(tarinfo)

        if tarinfo.isdir():
            self.addfile(tarinfo)
            if recursive:
                for f in os.listdir(name):
                    self.add(os.path.join(name, f), os.path.join(arcname, f))

    def addfile(self, tarinfo, fileobj=None):
        """Add the content of fileobj to the tarfile.
           The amount of bytes to read is determined by
           the size attribute in the tarinfo instance.
        """
        if not self.fileobj:
            raise ValueError, "I/O operation on closed file"
        if self._mode == "r":
            raise ValueError, "writing to a read-mode file"

        # XXX What was this good for again?
        #try:
        #    self.fileobj.seek(self.chunks[-1])
        #except IOError:
        #    pass

        full_headers = self._get_full_headers(tarinfo)
        self.fileobj.write(full_headers)
        assert len(full_headers) % BLOCKSIZE == 0
        self.offset += len(full_headers)

        # If there's data to follow, append it.
        if fileobj is not None:
            copyfileobj(fileobj, self.fileobj, tarinfo.size)
            blocks, remainder = divmod(tarinfo.size, BLOCKSIZE)
            if remainder > 0:
                self.fileobj.write("\0" * (BLOCKSIZE - remainder))
                blocks += 1
            self.offset += blocks * BLOCKSIZE

        #self.members.append(tarinfo)  #These take up too much memory
        #self.membernames.append(tarinfo.name)
        #self.chunks.append(self.offset)

    def _get_full_headers(self, tarinfo):
        """Return string containing headers around tarinfo, including gnulongs
        """
        buf = ""
        # Now we must check if the strings for filename
        # and linkname fit into the posix header.
        # (99 chars + "\0" for each)
        # If not, we must create GNU extension headers.
        # If both filename and linkname are too long,
        # the longlink is first to be written out.
        if len(tarinfo.linkname) >= LENGTH_LINK - 1:
            buf += self._return_gnulong(tarinfo.linkname, GNUTYPE_LONGLINK)
            tarinfo.linkname = tarinfo.linkname[:LENGTH_LINK -1]
        if len(tarinfo.name) >= LENGTH_NAME - 1:
            buf += self._return_gnulong(tarinfo.name, GNUTYPE_LONGNAME)
            tarinfo.name = tarinfo.name[:LENGTH_NAME - 1]
        return buf + tarinfo.getheader()

#    def untar(self, path):
#        """Untar the whole archive to path.
#        """
#        later = []
#        for tarinfo in self:
#            if tarinfo.isdir():
#                later.append(tarinfo)
#            self.extract(tarinfo, path)
#        for tarinfo in later:
#            self._utime(tarinfo, os.path.join(path, tarinfo.name))

    def extractfile(self, member):
        """Extract member from the archive and return a file-like
           object. member may be a name or a TarInfo instance.
        """
        if not self.fileobj:
            raise ValueError, "I/O operation on closed file"
        if self._mode != "r":
            raise ValueError, "reading from a write-mode file"

        if isinstance(member, TarInfo):
            tarinfo = member
        else:
            tarinfo = self.getmember(member)

        if tarinfo.isreg() or tarinfo.type not in SUPPORTED_TYPES:
            return _FileObject(self, tarinfo)
        elif tarinfo.islnk() or tarinfo.issym():
            return self.extractfile(self._getmember(tarinfo.linkname, tarinfo))
        else:
            return None

    def extract(self, member, path=""):
        """Extract member from the archive and write it to
           current working directory using its full pathname.
           If optional path is given, it is attached before the
           pathname.
           member may be a name or a TarInfo instance.
        """
        if not self.fileobj:
            raise ValueError, "I/O operation on closed file"
        if self._mode != "r":
            raise ValueError, "reading from a write-mode file"

        if isinstance(member, TarInfo):
            tarinfo = member
        else:
            tarinfo = self.getmember(member)

        self._dbg(1, tarinfo.name)
        try:
            self._extract_member(tarinfo, os.path.join(path, tarinfo.name))
        except EnvironmentError, e:
            if self.errorlevel > 0:
                raise
            else:
                self._dbg(1, "\ntarfile: %s `%s'" % (e.strerror, e.filename))
        except TarError, e:
            if self.errorlevel > 1:
                raise
            else:
                self._dbg(1, "\ntarfile: %s" % e)
        self._dbg(1, "\n")

    def _extract_member(self, tarinfo, targetpath):
        """Extract the TarInfo instance tarinfo to a physical
           file called targetpath.
        """
        # Fetch the TarInfo instance for the given name
        # and build the destination pathname, replacing
        # forward slashes to platform specific separators.
        if targetpath[-1:] == "/":
            targetpath = targetpath[:-1]
        targetpath = os.path.normpath(targetpath)

        # Create all upper directories.
        upperdirs = os.path.dirname(targetpath)
        if upperdirs and not os.path.exists(upperdirs):
            ti = TarInfo()
            ti.name  = ""
            ti.type  = DIRTYPE
            ti.mode  = 0777
            ti.mtime = tarinfo.mtime
            ti.uid   = tarinfo.uid
            ti.gid   = tarinfo.gid
            ti.uname = tarinfo.uname
            ti.gname = tarinfo.gname
            for d in os.path.split(os.path.splitdrive(upperdirs)[1]):
                ti.name = os.path.join(ti.name, d)
                self._extract_member(ti, ti.name)

        if tarinfo.isreg():
            self._makefile(tarinfo, targetpath)
        elif tarinfo.isdir():
            self._makedir(tarinfo, targetpath)
        elif tarinfo.isfifo():
            self._makefifo(tarinfo, targetpath)
        elif tarinfo.ischr() or tarinfo.isblk():
            self._makedev(tarinfo, targetpath)
        elif tarinfo.islnk() or tarinfo.issym():
            self._makelink(tarinfo, targetpath)
        else:
            self._makefile(tarinfo, targetpath)
            if tarinfo.type not in SUPPORTED_TYPES:
                self._dbg(1, "\ntarfile: Unknown file type '%s', " \
                             "extracted as regular file." % tarinfo.type)

        if not tarinfo.issym():
            self._chown(tarinfo, targetpath)
            self._chmod(tarinfo, targetpath)
            if not tarinfo.isdir():
                self._utime(tarinfo, targetpath)

    def _makedir(self, tarinfo, targetpath):
        """Make a directory called targetpath out of tarinfo.
        """
        try:
            os.mkdir(targetpath)
        except EnvironmentError, e:
            if e.errno != errno.EEXIST:
                raise

    def _makefile(self, tarinfo, targetpath):
        """Make a file called targetpath out of tarinfo.
        """
        source = self.extractfile(tarinfo)
        target = __builtin__.file(targetpath, "wb")
        copyfileobj(source, target)
        source.close()
        target.close()

    def _makefifo(self, tarinfo, targetpath):
        """Make a fifo called targetpath out of tarinfo.
        """
        if hasattr(os, "mkfifo"):
            os.mkfifo(targetpath)
        else:
            raise TarError, "Fifo not supported by system"

    def _makedev(self, tarinfo, targetpath):
        """Make a character or block device called targetpath out of tarinfo.
        """
        if not hasattr(os, "mknod"):
            raise TarError, "Special devices not supported by system"

        mode = tarinfo.mode
        if tarinfo.isblk():
            mode |= stat.S_IFBLK
        else:
            mode |= stat.S_IFCHR

        # This if statement should go away when python-2.3a0-devicemacros
        # patch succeeds.
        if hasattr(os, "makedev"):
            os.mknod(targetpath, mode,
                     os.makedev(tarinfo.devmajor, tarinfo.devminor))
        else:
            os.mknod(targetpath, mode,
                     tarinfo.devmajor, tarinfo.devminor)

    def _makelink(self, tarinfo, targetpath):
        """Make a (symbolic) link called targetpath out of tarinfo.
           If it cannot be made (due to platform or failure), we try
           to make a copy of the referenced file instead of a link.
        """
        linkpath = tarinfo.linkname
        self._dbg(1, " -> %s" % linkpath)
        try:
            if tarinfo.issym():
                os.symlink(linkpath, targetpath)
            else:
                linkpath = os.path.join(os.path.dirname(targetpath),
                                        linkpath)
                os.link(linkpath, targetpath)
        except AttributeError:
            linkpath = os.path.join(os.path.dirname(tarinfo.name),
                                    tarinfo.linkname)
            linkpath = normpath(linkpath)
            try:
                self._extract_member(self.getmember(linkpath), targetpath)
            except (IOError, OSError, KeyError), e:
                linkpath = os.path.normpath(linkpath)
                try:
                    shutil.copy2(linkpath, targetpath)
                except EnvironmentError, e:
                    raise TarError, "Link could not be created"

    def _chown(self, tarinfo, targetpath):
        """Set owner of targetpath according to tarinfo.
        """
        if pwd and os.geteuid() == 0:
            # We have to be root to do so.
            try: g = gname2gid(tarinfo.gname)
            except KeyError:
                try:
                    gid2gname(tarinfo.gid) # Make sure gid exists
                    g = tarinfo.gid
                except KeyError: g = os.getgid()
            try: u = uname2uid(tarinfo.uname)
            except KeyError:
                try:
                    uid2uname(tarinfo.uid) # Make sure uid exists
                    u = tarinfo.uid
                except KeyError: u = os.getuid()
            try:
                if tarinfo.issym() and hasattr(os, "lchown"):
                    os.lchown(targetpath, u, g)
                else:
                    os.chown(targetpath, u, g)
            except EnvironmentError, e:
                self._dbg(2, "\ntarfile: (chown failed), %s `%s'"
                             % (e.strerror, e.filename))

    def _chmod(self, tarinfo, targetpath):
        """Set file permissions of targetpath according to tarinfo.
        """
        try:
            os.chmod(targetpath, tarinfo.mode)
        except EnvironmentError, e:
            self._dbg(2, "\ntarfile: (chmod failed), %s `%s'"
                         % (e.strerror, e.filename))

    def _utime(self, tarinfo, targetpath):
        """Set modification time of targetpath according to tarinfo.
        """
        try:
            os.utime(targetpath, (tarinfo.mtime, tarinfo.mtime))
        except EnvironmentError, e:
            self._dbg(2, "\ntarfile: (utime failed), %s `%s'"
                         % (e.strerror, e.filename))

    def _getmember(self, name, tarinfo=None):
        """Find an archive member by name from bottom to top.
           If tarinfo is given, it is used as the starting point.
        """
        if tarinfo is None:
            end = len(self.members)
        else:
            end = self.members.index(tarinfo)

        for i in xrange(end - 1, -1, -1):
            if name == self.membernames[i]:
                return self.members[i]

    def _load(self):
        """Read through the entire archive file and look for readable
           members.
        """
        while 1:
            tarinfo = self.next()
            if tarinfo is None:
                break
        self._loaded = 1
        return

    def __iter__(self):
        """Provide an iterator object.
        """
        if self._loaded:
            return iter(self.members)
        else:
            return TarIter(self)

    def _buftoinfo(self, buf):
        """Transform a 512 byte block to a TarInfo instance.
        """
        tarinfo = TarInfo()
        tarinfo.name = nts(buf[0:100])
        tarinfo.mode = int(buf[100:107], 8)
        tarinfo.uid = int(buf[108:115],8)
        tarinfo.gid = int(buf[116:123],8)
        tarinfo.size = long(buf[124:135], 8)
        tarinfo.mtime = long(buf[136:147], 8)
		# chksum stored as a six digit octal number with
		# leading zeroes followed by a nul and then a space
        tarinfo.chksum = int(buf[148:154], 8)
        tarinfo.type = buf[156:157]
        tarinfo.linkname = nts(buf[157:257])
        tarinfo.uname = nts(buf[265:297])
        tarinfo.gname = nts(buf[297:329])
        try:
            tarinfo.devmajor = int(buf[329:337], 8)
            tarinfo.devminor = int(buf[337:345], 8)
        except ValueError:
            tarinfo.devmajor = tarinfo.devmajor = 0
        tarinfo.prefix = buf[345:500]
        if tarinfo.chksum != calc_chksum(buf):
            self._dbg(1, "tarfile: Bad Checksum\n")
        return tarinfo

    def _proc_gnulong(self, tarinfo, type):
        """Evaluate the two blocks that hold a GNU longname
           or longlink member.
        """
        name = None
        linkname = None
        buf = self.fileobj.read(BLOCKSIZE)
        if not buf: return None
        self.offset += BLOCKSIZE
        if type == GNUTYPE_LONGNAME: name = nts(buf)
        if type == GNUTYPE_LONGLINK: linkname = nts(buf)

        buf = self.fileobj.read(BLOCKSIZE)
        if not buf: return None
        tarinfo = self._buftoinfo(buf)
        if tarinfo.type in (GNUTYPE_LONGLINK, GNUTYPE_LONGNAME):
            tarinfo = self._proc_gnulong(tarinfo, tarinfo.type)
        if name is not None:
            tarinfo.name = name
        if linkname is not None:
            tarinfo.linkname = linkname
        self.offset += BLOCKSIZE
        return tarinfo

    def _return_gnulong(self, name, type):
        """Insert a GNU longname/longlink member into the archive.
           It consists of a common tar header, with the length
           of the longname as size, followed by a data block,
           which contains the longname as a null terminated string.
        """
        tarinfo = TarInfo()
        tarinfo.name = "././@LongLink"
        tarinfo.type = type
        tarinfo.mode = 0
        tarinfo.size = len(name)

        return "%s%s%s" % (tarinfo.getheader(), name,
                           "\0" * (512 - len(name)))

    def _proc_sparse(self, tarinfo):
        """Analyze a GNU sparse header plus extra headers.
        """
        buf = tarinfo.getheader()
        sp = _ringbuffer()
        pos = 386
        lastpos = 0l
        realpos = 0l
        try:
            # There are 4 possible sparse structs in the
            # first header.
            for i in range(4):
                offset = int(buf[pos:pos + 12], 8)
                numbytes = int(buf[pos + 12:pos + 24], 8)
                if offset > lastpos:
                    sp.append(_hole(lastpos, offset - lastpos))
                sp.append(_data(offset, numbytes, realpos))
                realpos += numbytes
                lastpos = offset + numbytes
                pos += 24

            isextended = ord(buf[482])
            origsize = int(buf[483:495], 8)

            # If the isextended flag is given,
            # there are extra headers to process.
            while isextended == 1:
                buf = self.fileobj.read(BLOCKSIZE)
                self.offset += BLOCKSIZE
                pos = 0
                for i in range(21):
                    offset = int(buf[pos:pos + 12], 8)
                    numbytes = int(buf[pos + 12:pos + 24], 8)
                    if offset > lastpos:
                        sp.append(_hole(lastpos, offset - lastpos))
                    sp.append(_data(offset, numbytes, realpos))
                    realpos += numbytes
                    lastpos = offset + numbytes
                    pos += 24
                isextended = ord(buf[504])
        except ValueError:
            pass
        if lastpos < origsize:
            sp.append(_hole(lastpos, origsize - lastpos))

        tarinfo.sparse = sp
        return origsize

    def _dbg(self, level, msg):
        if level <= self.debug:
            sys.stdout.write(msg)
# class TarFile

class TarIter:
    """Iterator Class.

       for tarinfo in TarFile(...):
           suite...
    """

    def __init__(self, tarfile):
        """Construct a TarIter instance.
        """
        self.tarfile = tarfile
    def __iter__(self):
        """Return iterator object.
        """
        return self
    def next(self):
        """Return the next item using TarFile's next() method.
           When all members have been read, set TarFile as _loaded.
        """
        tarinfo = self.tarfile.next()
        if not tarinfo:
            self.tarfile._loaded = 1
            raise StopIteration
        return tarinfo
# class TarIter

# Helper classes for sparse file support
class _section:
    """Base class for _data and _hole.
    """
    def __init__(self, offset, size):
        self.offset = offset
        self.size = size
    def __contains__(self, offset):
        return self.offset <= offset < self.offset + self.size

class _data(_section):
    """Represent a data section in a sparse file.
    """
    def __init__(self, offset, size, realpos):
        _section.__init__(self, offset, size)
        self.realpos = realpos

class _hole(_section):
    """Represent a hole section in a sparse file.
    """
    pass

class _ringbuffer(list):
    """Ringbuffer class which increases performance
       over a regular list.
    """
    def __init__(self):
        self.idx = 0
    def find(self, offset):
        idx = self.idx
        while 1:
            item = self[idx]
            if offset in item:
                break
            idx += 1
            if idx == len(self):
                idx = 0
            if idx == self.idx:
                # End of File
                return None
        self.idx = idx
        return item

class _FileObject:
    """File-like object for reading an archive member,
       is returned by TarFile.extractfile().
       Support for sparse files included.
    """

    def __init__(self, tarfile, tarinfo):
        self.tarfile = tarfile
        self.fileobj = tarfile.fileobj
        self.name    = tarinfo.name
        self.mode    = "r"
        self.closed  = 0
        self.offset  = tarinfo.offset_data
        self.size    = tarinfo.size
        self.pos     = 0l
        self.linebuffer = ""
        if tarinfo.issparse():
            self.sparse = tarinfo.sparse
            self.read = self._readsparse
        else:
            self.read = self._readnormal

    def readline(self, size=-1):
        """Read a line with approx. size.
           If size is negative, read a whole line.
           readline() and read() must not be mixed up (!).
        """
        if size < 0:
            size = sys.maxint

        nl = self.linebuffer.find("\n")
        if nl >= 0:
            nl = min(nl, size)
        else:
            size -= len(self.linebuffer)
            while nl < 0:
                buf = self.read(min(size, 100))
                if not buf:
                    break
                self.linebuffer += buf
                size -= len(buf)
                if size <= 0:
                    break
                nl = self.linebuffer.find("\n")
            if nl == -1:
                s = self.linebuffer
                self.linebuffer = ""
                return s
        buf = self.linebuffer[:nl]
        self.linebuffer = self.linebuffer[nl + 1:]
        while buf[-1:] == "\r":
            buf = buf[:-1]
        return buf + "\n"

    def readlines(self):
        """Return a list with all (following) lines.
        """
        result = []
        while 1:
            line = self.readline()
            if not line: break
            result.append(line)
        return result

    def _readnormal(self, size=None):
        """Read operation for regular files.
        """
        if self.closed:
            raise ValueError, "I/O operation on closed file"
        #self.fileobj.seek(self.offset + self.pos)
        bytesleft = self.size - self.pos
        if size is None:
            bytestoread = bytesleft
        else:
            bytestoread = min(size, bytesleft)
        self.pos += bytestoread
        self.tarfile.offset += bytestoread
        return self.fileobj.read(bytestoread)

    def _readsparse(self, size=None):
        """Read operation for sparse files.
        """
        if self.closed:
            raise ValueError, "I/O operation on closed file"

        if size is None:
            size = self.size - self.pos

        data = ""
        while size > 0:
            buf = self._readsparsesection(size)
            if not buf:
                break
            size -= len(buf)
            data += buf
        return data

    def _readsparsesection(self, size):
        """Read a single section of a sparse file.
        """
        section = self.sparse.find(self.pos)

        if section is None:
            return ""

        toread = min(size, section.offset + section.size - self.pos)
        if isinstance(section, _data):
            realpos = section.realpos + self.pos - section.offset
            self.pos += toread
            self.fileobj.seek(self.offset + realpos)
            return self.fileobj.read(toread)
        else:
            self.pos += toread
            return "\0" * toread

    def tell(self):
        """Return the current file position.
        """
        return self.pos

    def seek(self, pos, whence=0):
        """Seek to a position in the file.
        """
        self.linebuffer = ""
        if whence == 0:
            self.pos = min(max(pos, 0), self.size)
        if whence == 1:
            if pos < 0:
                self.pos = max(self.pos + pos, 0)
            else:
                self.pos = min(self.pos + pos, self.size)
        if whence == 2:
            self.pos = max(min(self.size + pos, self.size), 0)

    def close(self):
        """Close the file object.
        """
        self.closed = 1
#class _FileObject

#---------------------------------------------
# zipfile compatible TarFile class
#
# for details consult zipfile's documentation
#---------------------------------------------
import cStringIO

TAR_PLAIN = 0           # zipfile.ZIP_STORED
TAR_GZIPPED = 8         # zipfile.ZIP_DEFLATED
class TarFileCompat:
    """TarFile class compatible with standard module zipfile's
       ZipFile class.
    """
    def __init__(self, file, mode="r", compression=TAR_PLAIN):
        if compression == TAR_PLAIN:
            self.tarfile = open(file, mode)
        elif compression == TAR_GZIPPED:
            self.tarfile = gzopen(file, mode)
        else:
            raise ValueError, "unknown compression constant"
        if mode[0:1] == "r":
            import time
            members = self.tarfile.getmembers()
            for i in range(len(members)):
                m = members[i]
                m.filename = m.name
                m.file_size = m.size
                m.date_time = time.gmtime(m.mtime)[:6]
    def namelist(self):
        return map(lambda m: m.name, self.infolist())
    def infolist(self):
        return filter(lambda m: m.type in REGULAR_TYPES,
                      self.tarfile.getmembers())
    def printdir(self):
        self.tarfile.list()
    def testzip(self):
        return
    def getinfo(self, name):
        return self.tarfile.getmember(name)
    def read(self, name):
        return self.tarfile.extractfile(self.tarfile.getmember(name)).read()
    def write(self, filename, arcname=None, compress_type=None):
        self.tarfile.add(filename, arcname)
    def writestr(self, zinfo, bytes):
        import calendar
        zinfo.name = zinfo.filename
        zinfo.size = zinfo.file_size
        zinfo.mtime = calendar.timegm(zinfo.date_time)
        self.tarfile.addfile(zinfo, cStringIO.StringIO(bytes))
    def close(self):
        self.tarfile.close()
#class TarFileCompat

if __name__ == "__main__":
    # a "light-weight" implementation of GNUtar ;-)
    usage = """
Usage: %s [options] [files]

-h      display this help message
-c      create a tarfile
-r      append to an existing archive
-x      extract archive
-t      list archive contents
-f FILENAME
        use archive FILENAME, else STDOUT (-c)
-z      filter archive through gzip
-C DIRNAME
        with opt -x:     extract to directory DIRNAME
        with opt -c, -r: put files to archive under DIRNAME
-v      verbose output
-q      quiet

wildcards *, ?, [seq], [!seq] are accepted.
    """ % sys.argv[0]

    import getopt, glob
    try:
        opts, args = getopt.getopt(sys.argv[1:], "htcrzxf:C:qv")
    except getopt.GetoptError, e:
        print
        print "ERROR:", e
        print usage
        sys.exit(0)

    file = None
    mode = None
    dir = None
    comp = 0
    debug = 0
    for o, a in opts:
        if o == "-t": mode = "l"        # list archive
        if o == "-c": mode = "w"        # write to archive
        if o == "-r": mode = "a"        # append to archive
        if o == "-x": mode = "r"        # extract from archive
        if o == "-f": file = a          # specify filename else use stdout
        if o == "-C": dir = a           # change to dir
        if o == "-z": comp = 1          # filter through gzip
        if o == "-v": debug = 2         # verbose mode
        if o == "-q": debug = 0         # quiet mode
        if o == "-h":                   # help message
            print usage
            sys.exit(0)

    if not mode:
        print usage
        sys.exit(0)

    if comp:
        func = gzopen
    else:
        func = open

    if not file or file == "-":
        if mode != "w":
            print usage
            sys.exit(0)
        debug = 0
        # If under Win32, set stdout to binary.
        try:
            import msvcrt
            msvcrt.setmode(1, os.O_BINARY)
        except ImportError:
            pass
        tarfile = func("sys.stdout.tar", mode, 9, sys.stdout)
    else:
        if mode == "l":
            tarfile = func(file, "r")
        else:
            tarfile = func(file, mode)

    tarfile.debug = debug

    if mode == "r":
        if dir is None:
            dir = ""
        for tarinfo in tarfile:
            tarfile.extract(tarinfo, dir)
    elif mode == "l":
        tarfile.list(debug)
    else:
        for arg in args:
            files = glob.glob(arg)
            for f in files:
                tarfile.add(f, dir)
    tarfile.close()


class TarFromIterator(TarFile):
    """Readable tarfile-like object generated from iterator
    """
    # These various status numbers indicate what we are in the process
    # of doing in the tarfile.
    BEGIN = 0 # next step is to read tarinfo, write new header
    MIDDLE_OF_FILE = 1 # in process of writing file data
    END = 2 # end of data

    # Buffer is added to in multiples of following
    BUFFER_ADDLEN = 64 * 1024

    def __init__(self, pair_iter):
        """Construct a TarFromIterator instance.  pair_iter is an
        iterator of (TarInfo, fileobj) objects, which fileobj should
        be a file-like object opened for reading, or None.  The
        fileobjs will be closed before the next element in the
        iterator is read.
        """
        self.closed = None
        self.name = None
        self.mode = "rb"
        self.pair_iter = pair_iter

        self.init_datastructures()
        self.status = self.BEGIN
        self.cur_tarinfo, self.cur_fileobj = None, None
        self.cur_pos_in_fileobj = 0
        self.buffer = ""
        # holds current position as seen by reading client.  This is
        # distinct from self.offset.
        self.tar_iter_offset = 0

    def seek(self, offset):
        """Seek to current position.  Just read and discard some amount"""
        if offset < self.tar_iter_offset:
            raise TarError("Seeks in TarFromIterator must go forwards,\n"
                           "Instead asking for %s from %s" %
                           (offset, self.tar_iter_offset))
        while offset - self.tar_iter_offset >= self.BUFFER_ADDLEN:
            buf = self.read(self.BUFFER_ADDLEN)
            if not buf: return # eof
        self.read(offset - self.tar_iter_offset)

    def read(self, length = -1):
        """Return next length bytes, or everything if length < 0"""
        if length < 0:
            while 1:
                if not self._addtobuffer(): break
            result = self.buffer
            self.buffer = ""
        else:
            while len(self.buffer) < length:
                if not self._addtobuffer(): break
            # It's possible that length > len(self.buffer)
            result = self.buffer[:length]
            self.buffer = self.buffer[length:]
        self.tar_iter_offset += len(result)
        return result
        
    def _addtobuffer(self):
        """Write more data into the buffer.  Return None if at end"""
        if self.status == self.BEGIN:
            # Just write headers into buffer
            try: self.cur_tarinfo, self.cur_fileobj = self.pair_iter.next()
            except StopIteration:
                self._add_final()
                self.status = self.END
                return None

            # Zero out tarinfo sizes for various file types
            if self.cur_tarinfo.type in (LNKTYPE, SYMTYPE,
                                         FIFOTYPE, CHRTYPE, BLKTYPE):
                self.cur_tarinfo.size = 0l

            full_headers = self._get_full_headers(self.cur_tarinfo)
            self.buffer += full_headers
            self.offset += len(full_headers)
            assert len(full_headers) % BLOCKSIZE == 0

            if self.cur_fileobj is None: # no data with header
                self.status = self.BEGIN
                self._finish_fileobj()
            else:
                self.status = self.MIDDLE_OF_FILE
                self.cur_pos_in_fileobj = 0
            return 1
        elif self.status == self.MIDDLE_OF_FILE:
            # Add next chunk of self.cur_fileobj to self.buffer
            l = min(self.BUFFER_ADDLEN,
                    self.cur_tarinfo.size - self.cur_pos_in_fileobj)
            s = self.cur_fileobj.read(l)
            self.cur_pos_in_fileobj += len(s)
            if len(s) == 0:
                if l != 0: raise IOError, "end of file reached"
                blocks, remainder = divmod(self.cur_tarinfo.size, BLOCKSIZE)
                if remainder > 0:
                    self.buffer += "\0" * (BLOCKSIZE - remainder)
                    blocks += 1
                self.cur_fileobj.close()
                self.offset += blocks * BLOCKSIZE
                self._finish_fileobj()
                self.status = self.BEGIN
            else: self.buffer += s
            return 1
        elif self.status == self.END: return None
        assert 0

    def _finish_fileobj(self):
        """Update some variables when done writing fileobj"""
        return # Skip saving tarinfo information to save memory
        self.members.append(self.cur_tarinfo)
        self.membernames.append(self.cur_tarinfo.name)
        self.chunks.append(self.offset)

    def _add_final(self):
        """Add closing footer to buffer"""
        blocks, remainder = divmod(self.offset, RECORDSIZE)
        if remainder > 0: self.buffer += "\0" * (RECORDSIZE - remainder)

    def close(self):
        """Close file obj"""
        assert not self.closed
        self.closed = 1


def uid2uname(uid):
    """Return uname of uid, or raise KeyError if none"""
    return "root"
    if uid_dict is None: set_pwd_dict()
    return uid_dict[uid]

def uname2uid(uname):
    """Return uid of given uname, or raise KeyError if none"""
    return 0
    if uname_dict is None: set_pwd_dict()
    return uname_dict[uname]

def set_pwd_dict():
    """Set global pwd caching dictionaries uid_dict and uname_dict"""
    global uid_dict, uname_dict
    assert uid_dict is None and uname_dict is None and pwd
    uid_dict = {}; uname_dict = {}
    for entry in pwd.getpwall():
        uname = entry[0]; uid = entry[2]
        uid_dict[uid] = uname
        uname_dict[uname] = uid

def gid2gname(gid):
    """Return group name of gid, or raise KeyError if none"""
    return "wheel"
    if gid_dict is None: set_grp_dict()
    return gid_dict[gid]

def gname2gid(gname):
    """Return gid of given group name, or raise KeyError if none"""
    return 0
    if gname_dict is None: set_grp_dict()
    return gname_dict[gname]

def set_grp_dict():
    global gid_dict, gname_dict
    assert gid_dict is None and gname_dict is None and grp
    gid_dict = {}; gname_dict = {}
    for entry in grp.getgrall():
        gname = entry[0]; gid = entry[2]
        gid_dict[gid] = gname
        gname_dict[gname] = gid
