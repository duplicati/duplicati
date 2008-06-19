# Copyright 2002 Ben Escoto
#
# This file is part of duplicity.
#
# duplicity is free software; you can redistribute it and/or modify
# under the terms of the GNU General Public License as published by
# the Free Software Foundation; either version 3 of the License, or
# (at your option) any later version.
#
# duplicity is distributed in the hope that it will be useful, but
# WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
# General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with duplicity; if not, write to the Free Software Foundation,
# Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

"""Provides a high-level interface to some librsync functions

This is a python wrapper around the lower-level _librsync module,
which is written in C.  The goal was to use C as little as possible...

"""

import _librsync, types, array

blocksize = _librsync.RS_JOB_BLOCKSIZE

class librsyncError(Exception):
	"""Signifies error in internal librsync processing (bad signature, etc.)

	underlying _librsync.librsyncError's are regenerated using this
	class because the C-created exceptions are by default
	unPickleable.  There is probably a way to fix this in _librsync,
	but this scheme was easier.

	"""
	pass


class LikeFile:
	"""File-like object used by SigFile, DeltaFile, and PatchFile"""
	mode = "rb"

	# This will be replaced in subclasses by an object with
	# appropriate cycle() method
	maker = None

	def __init__(self, infile, need_seek = None):
		"""LikeFile initializer - zero buffers, set eofs off"""
		self.check_file(infile, need_seek)
		self.infile = infile
		self.closed = self.infile_closed = None
		self.inbuf = ""
		self.outbuf = array.array('c')
		self.eof = self.infile_eof = None

	def check_file(self, file, need_seek = None):
		"""Raise type error if file doesn't have necessary attributes"""
		if not hasattr(file, "read"):
			raise TypeError("Basis file must have a read() method")
		if not hasattr(file, "close"):
			raise TypeError("Basis file must have a close() method")
		if need_seek and not hasattr(file, "seek"):
			raise TypeError("Basis file must have a seek() method")

	def read(self, length = -1):
		"""Build up self.outbuf, return first length bytes"""
		if length == -1:
			while not self.eof: self._add_to_outbuf_once()
			real_len = len(self.outbuf)
		else:
			while not self.eof and len(self.outbuf) < length:
				self._add_to_outbuf_once()
			real_len = min(length, len(self.outbuf))
			
		return_val = self.outbuf[:real_len].tostring()
		del self.outbuf[:real_len]
		return return_val

	def _add_to_outbuf_once(self):
		"""Add one cycle's worth of output to self.outbuf"""
		if not self.infile_eof: self._add_to_inbuf()
		try: self.eof, len_inbuf_read, cycle_out = self.maker.cycle(self.inbuf)
		except _librsync.librsyncError, e: raise librsyncError(str(e))
		self.inbuf = self.inbuf[len_inbuf_read:]
		self.outbuf.fromstring(cycle_out)

	def _add_to_inbuf(self):
		"""Make sure len(self.inbuf) >= blocksize"""
		assert not self.infile_eof
		while len(self.inbuf) < blocksize:
			new_in = self.infile.read(blocksize)
 			if not new_in:
				self.infile_eof = 1
				assert not self.infile.close()
				self.infile_closed = 1
				break
			self.inbuf += new_in

	def close(self):
		"""Close infile"""
		if not self.infile_closed: assert not self.infile.close()
		self.closed = 1


class SigFile(LikeFile):
	"""File-like object which incrementally generates a librsync signature"""
	def __init__(self, infile, blocksize = _librsync.RS_DEFAULT_BLOCK_LEN):
		"""SigFile initializer - takes basis file

		basis file only needs to have read() and close() methods.  It
		will be closed when we come to the end of the signature.

		"""
		LikeFile.__init__(self, infile)
		try: self.maker = _librsync.new_sigmaker(blocksize)
		except _librsync.librsyncError, e: raise librsyncError(str(e))

class DeltaFile(LikeFile):
	"""File-like object which incrementally generates a librsync delta"""
	def __init__(self, signature, new_file):
		"""DeltaFile initializer - call with signature and new file

		Signature can either be a string or a file with read() and
		close() methods.  New_file also only needs to have read() and
		close() methods.  It will be closed when self is closed.

		"""
		LikeFile.__init__(self, new_file)
		if type(signature) is types.StringType: sig_string = signature
		else:
			self.check_file(signature)
			sig_string = signature.read()
			assert not signature.close()
		try: self.maker = _librsync.new_deltamaker(sig_string)
		except _librsync.librsyncError, e: raise librsyncError(str(e))


class PatchedFile(LikeFile):
	"""File-like object which applies a librsync delta incrementally"""
	def __init__(self, basis_file, delta_file):
		"""PatchedFile initializer - call with basis delta

		Here basis_file must be a true Python file, because we may
		need to seek() around in it a lot, and this is done in C.
		delta_file only needs read() and close() methods.

		"""
		LikeFile.__init__(self, delta_file)
		if type(basis_file) is not types.FileType:
			raise TypeError("basis_file must be a (true) file")
		try: self.maker = _librsync.new_patchmaker(basis_file)
		except _librsync.librsyncError, e: raise librsyncError(str(e))		


class SigGenerator:
	"""Calculate signature.

	Input and output is same as SigFile, but the interface is like md5
	module, not filelike object

	"""
	def __init__(self, blocksize = _librsync.RS_DEFAULT_BLOCK_LEN):
		"""Return new signature instance"""
		try: self.sig_maker = _librsync.new_sigmaker(blocksize)
		except _librsync.librsyncError, e: raise librsyncError(str(e))
		self.gotsig = None
		self.buffer = ""
		self.sig_string = ""

	def update(self, buf):
		"""Add buf to data that signature will be calculated over"""
		if self.gotsig:
			raise librsyncError("SigGenerator already provided signature")
		self.buffer += buf
		while len(self.buffer) >= blocksize:
			if self.process_buffer():
				raise librsyncError("Premature EOF received from sig_maker")

	def process_buffer(self):
		"""Run self.buffer through sig_maker, add to self.sig_string"""
		try: eof, len_buf_read, cycle_out = self.sig_maker.cycle(self.buffer)
		except _librsync.librsyncError, e: raise librsyncError(str(e))
		self.buffer = self.buffer[len_buf_read:]
		self.sig_string += cycle_out
		return eof

	def getsig(self):
		"""Return signature over given data"""
		while not self.process_buffer(): pass # keep running until eof
		return self.sig_string

