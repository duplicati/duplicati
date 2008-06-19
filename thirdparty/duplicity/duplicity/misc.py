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

"""Miscellaneous classes and methods"""

import os
import log

class MiscError(Exception):
	"""Signifies a miscellaneous error..."""
	pass


class FileVolumeWriter:
	"""Split up an incoming fileobj into multiple volumes on disk

	This class can also be used as an iterator.  It returns the
	filenames of the files it writes.

	"""
	volume_size = 50 * 1024 * 1024
	blocksize = 64 * 1024
	def __init__(self, infp, file_prefix):
		"""FileVolumeWriter initializer

		infp is a file object opened for reading.  It will be closed
		at end.  file_prefix is the full path of the volumes that will
		be written.  If more than one is required, it will be appended
		with .1, .2, etc.

		"""
		self.infp = infp
		self.prefix = file_prefix
		self.current_index = 1
		self.finished = None # set to true when completely done
		self.buffer = "" # holds data that belongs in next volume

	def get_initial_buf(self):
		"""Get first value of buffer, from self.buffer or infp"""
		if self.buffer:
			buf = self.buffer
			self.buffer = ""
			return buf
		else: return self.infp.read(self.blocksize)

	def write_volume(self, outfp):
		"""Write self.volume_size bytes from self.infp to outfp

		Return None if we have reached end of infp without reaching
		volume size, and false otherwise.

		"""
		bytes_written, buf = 0, self.get_initial_buf()
		while len(buf) + bytes_written <= self.volume_size:
			if not buf: # reached end of input
				outfp.close()
				return None
			if len(buf) + bytes_written > self.volume_size: break
			outfp.write(buf)
			bytes_written += len(buf)
			buf = self.infp.read(self.blocksize)

		remainder = self.volume_size - bytes_written
		assert remainder < len(buf)
		outfp.write(buf[:remainder])
		outfp.close()
		self.buffer = buf[remainder:]
		return 1

	def next(self):
		"""Write next file, return filename"""
		if self.finished: raise StopIteration

		filename = "%s.%d" % (self.prefix, self.current_index)
		log.Log("Starting to write %s" % filename, 5)
		outfp = open(filename, "wb")

		if not self.write_volume(outfp): # end of input
			self.finished = 1
			if self.current_index == 1: # special case first index
				log.Log("One only volume required.\n"
						"Renaming %s to %s" % (filename, self.prefix), 4)
				os.rename(filename, self.prefix)
				return self.prefix
		else: self.current_index += 1
		return filename

	def __iter__(self): return self


class BufferedFile:
	"""Buffer file open for reading, so reads will happen in fixed sizes

	This is currently used to buffer a GzipFile, because that class
	apparently doesn't respond well to arbitrary read sizes.

	"""
	def __init__(self, fileobj, blocksize = 32 * 1024):
		self.fileobj = fileobj
		self.buffer = ""
		self.blocksize = blocksize

	def read(self, length = -1):
		"""Return length bytes, or all if length < 0"""
		if length < 0:
			while 1:
				buf = self.fileobj.read(self.blocksize)
				if not buf: break
				self.buffer += buf
			real_length = len(self.buffer)
		else:
			while len(self.buffer) < length:
				buf = self.fileobj.read(self.blocksize)
				if not buf: break
				self.buffer += buf
			real_length = min(length, len(self.buffer))
		result = self.buffer[:real_length]
		self.buffer = self.buffer[real_length:]
		return result

	def close(self): self.fileobj.close()
		
	
def copyfileobj(infp, outfp, byte_count = -1):
	"""Copy byte_count bytes from infp to outfp, or all if byte_count < 0

	Returns the number of bytes actually written (may be less than
	byte_count if find eof.  Does not close either fileobj.

	"""
	blocksize = 64 * 1024
	bytes_written = 0
	if byte_count < 0:
		while 1:
			buf = infp.read(blocksize)
			if not buf: break
			bytes_written += len(buf)
			outfp.write(buf)
	else:
		while bytes_written + blocksize <= byte_count:
			buf = infp.read(blocksize)
			if not buf: break
			bytes_written += len(buf)
			outfp.write(buf)
		buf = infp.read(byte_count - bytes_written)
		bytes_written += len(buf)
		outfp.write(buf)
	return bytes_written

def copyfileobj_close(infp, outfp):
	"""Copy infp to outfp, closing afterwards"""
	copyfileobj(infp, outfp)
	if infp.close(): raise MiscError("Error closing input file")
	if outfp.close(): raise MiscError("Error closing output file")


