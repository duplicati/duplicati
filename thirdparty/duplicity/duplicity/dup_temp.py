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

"""Manage temporary files"""

import tempfile
import log, path, file_naming

import duplicity.tempdir as tempdir

def new_temppath():
	"""Return a new TempPath"""
	filename = tempdir.default().mktemp()
	return TempPath(filename)

class TempPath(path.Path):
	"""Path object used as a temporary file"""
	def delete(self):
		"""Forget and delete"""
		path.Path.delete(self)
		tempdir.default().forget(self.name)

	def open_with_delete(self, mode):
		"""Returns a fileobj.  When that is closed, delete file"""
		fh = FileobjHooked(path.Path.open(self, mode))
		fh.addhook(self.delete)
		return fh

def get_fileobj_duppath(dirpath, filename):
	"""Return a file object open for writing, will write to filename

	Data will be processed and written to a temporary file.  When the
	return fileobject is closed, rename to final position.  filename
	must be a recognizable duplicity data file.
	"""
	td = tempdir.TemporaryDirectory(dirpath.name)
	tdpname = td.mktemp()
	tdp = TempDupPath(tdpname, parseresults = file_naming.parse(filename))
	
	fh = FileobjHooked(tdp.filtered_open("wb"))
	def rename_and_forget():
		tdp.rename(dirpath.append(filename))
		td.forget(tdpname)

	fh.addhook(rename_and_forget)

	return fh

def new_tempduppath(parseresults):
	"""Return a new TempDupPath, using settings from parseresults"""
	filename = tempdir.default().mktemp()
	return TempDupPath(filename, parseresults = parseresults)

class TempDupPath(path.DupPath):
	"""Like TempPath, but build around DupPath"""
	def delete(self):
		"""Forget and delete"""
		path.DupPath.delete(self)
		tempdir.default().forget(self.name)

	def filtered_open_with_delete(self, mode):
		"""Returns a filtered fileobj.  When that is closed, delete file"""
		fh = FileobjHooked(path.DupPath.filtered_open(self, mode))
		fh.addhook(self.delete)
		return fh

	def open_with_delete(self, mode = "rb"):
		"""Returns a fileobj.  When that is closed, delete file"""
		assert mode == "rb" # Why write a file and then close it immediately?
		fh = FileobjHooked(path.DupPath.open(self, mode))
		fh.addhook(self.delete)
		return fh

class FileobjHooked:
	"""Simulate a file, but add hook on close"""
	def __init__(self, fileobj):
		"""Initializer.  fileobj is the file object to simulate"""
		self.fileobj = fileobj
		self.closed = None
		self.hooklist = [] # fill later with thunks to run on close
		# self.second by MDR.  Will be filled by addfilehandle -- poor mans tee
		self.second = None

	def write(self, buf):
		if self.second: self.second.write(buf) # by MDR.  actual tee
		return self.fileobj.write(buf)
	
	def read(self, length = -1): return self.fileobj.read(length)

	def close(self):
		"""Close fileobj, running hooks right afterwards"""
		assert not self.fileobj.close()
		if self.second: assert not self.second.close()
		for hook in self.hooklist: hook()

	def addhook(self, hook):
		"""Add hook (function taking no arguments) to run upon closing"""
		self.hooklist.append(hook)

	def addfilehandle(self, fh): # by MDR
		"""Add a second filehandle for listening to the input
		
		This only works properly for two write handles"""
		assert not self.second
		self.second = fh
