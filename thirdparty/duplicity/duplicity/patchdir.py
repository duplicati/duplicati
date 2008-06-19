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

"""Functions for patching of directories"""

from __future__ import generators
import re, tarfile, librsync, log, diffdir, misc
from path import *
from lazy import *

class PatchDirException(Exception): pass


def Patch(base_path, difftar_fileobj):
	"""Patch given base_path and file object containing delta"""
	diff_tarfile = tarfile.TarFile("arbitrary", "r", difftar_fileobj)
	patch_diff_tarfile(base_path, diff_tarfile)
	assert not difftar_fileobj.close()

def Patch_from_iter(base_path, fileobj_iter, restrict_index = ()):
	"""Patch given base_path and iterator of delta file objects"""
	diff_tarfile = TarFile_FromFileobjs(fileobj_iter)
	patch_diff_tarfile(base_path, diff_tarfile, restrict_index)

def patch_diff_tarfile(base_path, diff_tarfile, restrict_index = ()):
	"""Patch given Path object using delta tarfile (as in tarfile.TarFile)

	If restrict_index is set, ignore any deltas in diff_tarfile that
	don't start with restrict_index.

	"""
	if base_path.exists(): path_iter = selection.Select(base_path).set_iter()
	else: path_iter = empty_iter() # probably untarring full backup

	diff_path_iter = difftar2path_iter(diff_tarfile)
	if restrict_index:
		diff_path_iter = filter_path_iter(diff_path_iter, restrict_index)
	collated = diffdir.collate2iters(path_iter, diff_path_iter)

	ITR = IterTreeReducer(PathPatcher, [base_path])
	for basis_path, diff_ropath in collated:
		if basis_path:
			log.Log("Patching %s" % (basis_path.get_relative_path(),), 5)
			ITR(basis_path.index, basis_path, diff_ropath)
		else:
			log.Log("Patching %s" % (diff_ropath.get_relative_path(),), 5)
			ITR(diff_ropath.index, basis_path, diff_ropath)
	ITR.Finish()
	base_path.setdata()

def empty_iter():
	if 0: yield 1 # this never happens, but fools into generator treatment

def filter_path_iter(path_iter, index):
	"""Rewrite path elements of path_iter so they start with index

	Discard any that doesn't start with index, and remove the index
	prefix from the rest.

	"""
	assert isinstance(index, tuple) and index, index
	l = len(index)
	for path in path_iter:
		if path.index[:l] == index:
			path.index = path.index[l:]
			yield path

def difftar2path_iter(diff_tarfile):
	"""Turn file-like difftarobj into iterator of ROPaths"""
	tar_iter = iter(diff_tarfile)
	multivol_fileobj = None

	# The next tar_info is stored in this one element list so
	# Multivol_Filelike below can update it.  Any StopIterations will
	# be passed upwards.
	tarinfo_list = [tar_iter.next()]

	while 1:
		# This section relevant when a multivol diff is last in tar
		if not tarinfo_list[0]: raise StopIteration
		if multivol_fileobj and not multivol_fileobj.at_end:
			multivol_fileobj.close() # aborting in middle of multivol
			continue
		
		index, difftype, multivol = get_index_from_tarinfo(tarinfo_list[0])
		ropath = ROPath(index)
		ropath.init_from_tarinfo(tarinfo_list[0])
		ropath.difftype = difftype
		if difftype == "deleted": ropath.type = None
		elif ropath.isreg():
			if multivol:
				multivol_fileobj = Multivol_Filelike(diff_tarfile, tar_iter,
													 tarinfo_list, index)
				ropath.setfileobj(multivol_fileobj)
				yield ropath
				continue # Multivol_Filelike will reset tarinfo_list
			else: ropath.setfileobj(diff_tarfile.extractfile(tarinfo_list[0]))
		yield ropath
		tarinfo_list[0] = tar_iter.next()

def get_index_from_tarinfo(tarinfo):
	"""Return (index, difftype, multivol) pair from tarinfo object"""
	for prefix in ["snapshot/", "diff/", "deleted/",
				   "multivol_diff/", "multivol_snapshot/"]:
		if tarinfo.name.startswith(prefix):
			name = tarinfo.name[len(prefix):] # strip prefix
			if prefix.startswith("multivol"):
				if prefix == "multivol_diff/": difftype = "diff"
				else: difftype = "snapshot"
				multivol = 1
				name, num_subs = \
					  re.subn("^multivol_(diff|snapshot)/(.*)/[0-9]+$",
							  "\\2", tarinfo.name)
				if num_subs != 1:
					raise PatchDirException("Unrecognized diff entry %s" %
											(tarinfo.name,))
			else:
				difftype = prefix[:-1] # strip trailing /
				name = tarinfo.name[len(prefix):]
				if name.endswith("/"): name = name[:-1] # strip trailing /'s
				multivol = 0
			break
	else: raise PatchDirException("Unrecognized diff entry %s" %
								 (tarinfo.name,))
	if name == "." or name == "": index = ()
	else:
		index = tuple(name.split("/"))
		if '..' in index:
			raise PatchDirException("Tar entry %s contains '..'.  Security "
									"violation" % (tarinfo.name,))
	return (index, difftype, multivol)


class Multivol_Filelike:
	"""Emulate a file like object from multivols

	Maintains a buffer about the size of a volume.  When it is read()
	to the end, pull in more volumes as desired.

	"""
	def __init__(self, tf, tar_iter, tarinfo_list, index):
		"""Initializer.  tf is TarFile obj, tarinfo is first tarinfo"""
		self.tf, self.tar_iter = tf, tar_iter
		self.tarinfo_list = tarinfo_list # must store as list for write access
		self.index = index
		self.buffer = ""
		self.at_end = 0

	def read(self, length = -1):
		"""Read length bytes from file"""
		if length < 0:
			while self.addtobuffer(): pass
			real_len = len(self.buffer)
		else:
			while len(self.buffer) < length:
				if not self.addtobuffer(): break
			real_len = min(len(self.buffer), length)

		result = self.buffer[:real_len]
		self.buffer = self.buffer[real_len:]
		return result

	def addtobuffer(self):
		"""Add next chunk to buffer"""
		if self.at_end: return None
		index, difftype, multivol = get_index_from_tarinfo(
			self.tarinfo_list[0])
		if not multivol or index != self.index: # we've moved on
			# the following communicates next tarinfo to difftar2path_iter
			self.at_end = 1
			return None

		fp = self.tf.extractfile(self.tarinfo_list[0])
		self.buffer += fp.read()
		fp.close()

		try: self.tarinfo_list[0] = self.tar_iter.next()
		except StopIteration:
			self.tarinfo_list[0] = None
			self.at_end = 1
			return None
		return 1

	def close(self):
		"""If not at end, read remaining data"""
		if not self.at_end:
			while 1:
				self.buffer = ""
				if not self.addtobuffer(): break
		self.at_end = 1
		

class PathPatcher(ITRBranch):
	"""Used by DirPatch, process the given basis and diff"""
	def __init__(self, base_path):
		"""Set base_path, Path of root of tree"""
		self.base_path = base_path
		self.dir_diff_ropath = None

	def start_process(self, index, basis_path, diff_ropath):
		"""Start processing when diff_ropath is a directory"""
		if not (diff_ropath and diff_ropath.isdir()):
			assert index == (), str(index) # should only happen for first elem
			self.fast_process(index, basis_path, diff_ropath)
			return
			
		if not basis_path:
			basis_path = self.base_path.new_index(index)
			assert not basis_path.exists()
			basis_path.mkdir() # Need place for later files to go into
		elif not basis_path.isdir():
			basis_path.delete()
			basis_path.mkdir()
		self.dir_basis_path = basis_path
		self.dir_diff_ropath = diff_ropath

	def end_process(self):
		"""Copy directory permissions when leaving tree"""
		if self.dir_diff_ropath:
			self.dir_diff_ropath.copy_attribs(self.dir_basis_path)

	def can_fast_process(self, index, basis_path, diff_ropath):
		"""No need to recurse if diff_ropath isn't a directory"""
		return not (diff_ropath and diff_ropath.isdir())

	def fast_process(self, index, basis_path, diff_ropath):
		"""For use when neither is a directory"""
		if not diff_ropath: return # no change
		elif not basis_path:
			if diff_ropath.difftype == "deleted": pass # already deleted
			else:  # just copy snapshot over
				diff_ropath.copy(self.base_path.new_index(index))
		elif diff_ropath.difftype == "deleted":
			if basis_path.isdir(): basis_path.deltree()
			else: basis_path.delete()
		elif not basis_path.isreg():
			if basis_path.isdir(): basis_path.deltree()
			else: basis_path.delete()
			diff_ropath.copy(basis_path)
		else:
			assert diff_ropath.difftype == "diff", diff_ropath.difftype
			basis_path.patch_with_attribs(diff_ropath)


class TarFile_FromFileobjs:
	"""Like a tarfile.TarFile iterator, but read from multiple fileobjs"""
	def __init__(self, fileobj_iter):
		"""Make new tarinfo iterator

		fileobj_iter should be an iterator of file objects opened for
		reading.  They will be closed at end of reading.

		"""
		self.fileobj_iter = fileobj_iter
		self.tarfile, self.tar_iter = None, None
		self.current_fp = None

	def __iter__(self): return self

	def set_tarfile(self):
		"""Set tarfile from next file object, or raise StopIteration"""
		if self.current_fp: assert not self.current_fp.close()
		self.current_fp = self.fileobj_iter.next()
		self.tarfile = tarfile.TarFile("arbitrary", "r", self.current_fp)
		self.tar_iter = iter(self.tarfile)

	def next(self):
		if not self.tarfile: self.set_tarfile()
		try: return self.tar_iter.next()
		except StopIteration:
			assert not self.tarfile.close()
			self.set_tarfile()
			return self.tar_iter.next()

	def extractfile(self, tarinfo):
		"""Return data associated with given tarinfo"""
		return self.tarfile.extractfile(tarinfo)


def collate_iters(iter_list):
	"""Collate iterators by index

	Input is a list of n iterators each of which must iterate elements
	with an index attribute.  The elements must come out in increasing
	order, and the index should be a tuple itself.

	The output is an iterator which yields tuples where all elements
	in the tuple have the same index, and the tuple has n elements in
	it.  If any iterator lacks an element with that index, the tuple
	will have None in that spot.

	"""
	# overflow[i] means that iter_list[i] has been exhausted
	# elems[i] is None means that it is time to replenish it.
	iter_num = len(iter_list)
	if iter_num == 2:
		return diffdir.collate2iters(iter_list[0], iter_list[1])
	overflow = [None] * iter_num
	elems = overflow[:]

	def setrorps(overflow, elems):
		"""Set the overflow and rorps list"""
		for i in range(iter_num):
			if not overflow[i] and elems[i] is None:
				try: elems[i] = iter_list[i].next()
				except StopIteration:
					overflow[i] = 1
					elems[i] = None

	def getleastindex(elems):
		"""Return the first index in elems, assuming elems isn't empty"""
		return min(map(lambda elem: elem.index, filter(lambda x: x, elems)))

	def yield_tuples(iter_num, overflow, elems):
		while 1:
			setrorps(overflow, elems)
			if not None in overflow: break

			index = getleastindex(elems)
			yieldval = []
			for i in range(iter_num):
				if elems[i] and elems[i].index == index:
					yieldval.append(elems[i])
					elems[i] = None
				else: yieldval.append(None)
			yield tuple(yieldval)
	return yield_tuples(iter_num, overflow, elems)

class IndexedTuple:
	"""Like a tuple, but has .index (used previously by collate_iters)"""
	def __init__(self, index, sequence):
		self.index = index
		self.data = tuple(sequence)

	def __len__(self): return len(self.data)

	def __getitem__(self, key):
		"""This only works for numerical keys (easier this way)"""
		return self.data[key]

	def __lt__(self, other): return self.__cmp__(other) == -1
	def __le__(self, other): return self.__cmp__(other) != 1
	def __ne__(self, other): return not self.__eq__(other)
	def __gt__(self, other): return self.__cmp__(other) == 1
	def __ge__(self, other): return self.__cmp__(other) != -1
	
	def __cmp__(self, other):
		assert isinstance(other, IndexedTuple)
		if self.index < other.index: return -1
		elif self.index == other.index: return 0
		else: return 1

	def __eq__(self, other):
		if isinstance(other, IndexedTuple):
			return self.index == other.index and self.data == other.data
		elif type(other) is types.TupleType:
			return self.data == other
		else: return None

	def __str__(self):
		return  "(%s).%s" % (", ".join(map(str, self.data)), self.index)

def normalize_ps(patch_sequence):
	"""Given an sequence of ROPath deltas, remove blank and unnecessary

	The sequence is assumed to be in patch order (later patches apply
	to earlier ones).  A patch is unnecessary if a later one doesn't
	require it (for instance, any patches before a "delete" are
	unnecessary).

	"""
	result_list = []
	i = len(patch_sequence)-1
	while i >= 0:
		delta = patch_sequence[i]
		if delta is not None: # skip blank entries
			result_list.insert(0, delta)
			if delta.difftype != "diff": break
		i -= 1
	return result_list

def patch_seq2ropath(patch_seq):
	"""Apply the patches in patch_seq, return single ropath"""
	first = patch_seq[0]
	assert first.difftype != "diff", patch_seq
	if not first.isreg(): # No need to bother with data if not regular file
		assert len(patch_seq) == 1, len(patch_seq)
		return first.get_ropath()

	current_file = first.open("rb")

	for delta_ropath in patch_seq[1:]:
		assert delta_ropath.difftype == "diff", delta_ropath.difftype
		if not isinstance(current_file, file): # librsync needs true file
			tempfp = os.tmpfile()
			misc.copyfileobj(current_file, tempfp)
			assert not current_file.close()
			tempfp.seek(0)
			current_file = tempfp
		current_file = librsync.PatchedFile(current_file,
											delta_ropath.open("rb"))
	result = patch_seq[-1].get_ropath()
	result.setfileobj(current_file)
	return result

def integrate_patch_iters(iter_list):
	"""Combine a list of iterators of ropath patches

	The iter_list should be sorted in patch order, and the elements in
	each iter_list need to be orderd by index.  The output will be an
	iterator of the final ROPaths in index order.

	"""
	collated = collate_iters(iter_list)
	for patch_seq in collated:
		final_ropath = patch_seq2ropath(normalize_ps(patch_seq))
		if final_ropath.exists(): # otherwise final patch was delete
			yield final_ropath

def tarfiles2rop_iter(tarfile_list, restrict_index = ()):
	"""Integrate tarfiles of diffs into single ROPath iter

	Then filter out all the diffs in that index which don't start with
	the restrict_index.

	"""
	diff_iters = map(difftar2path_iter, tarfile_list)
	if restrict_index: # Apply filter before integration
		diff_iters = map(lambda i: filter_path_iter(i, restrict_index),
						 diff_iters)
	return integrate_patch_iters(diff_iters)

def Write_ROPaths(base_path, rop_iter):
	"""Write out ropaths in rop_iter starting at base_path

	Returns 1 if something was actually written, 0 otherwise.

	"""
	ITR = IterTreeReducer(ROPath_IterWriter, [base_path])
	return_val = 0
	for ropath in rop_iter:
		return_val = 1
		ITR(ropath.index, ropath)
	ITR.Finish()
	base_path.setdata()
	return return_val

class ROPath_IterWriter(ITRBranch):
	"""Used in Write_ROPaths above

	We need to use an ITR because we have to update the
	permissions/times of directories after we write the files in them.

	"""
	def __init__(self, base_path):
		"""Set base_path, Path of root of tree"""
		self.base_path = base_path
		self.dir_diff_ropath = None
		self.dir_new_path = None

	def start_process(self, index, ropath):
		"""Write ropath.  Only handles the directory case"""
		if not ropath.isdir(): # Base may not be a directory, but rest should
			assert ropath.index == (), ropath.index
			new_path = self.base_path.new_index(index)
			if ropath.exists():
				if new_path.exists(): new_path.deltree()
				ropath.copy(new_path)

		self.dir_new_path = self.base_path.new_index(index)
		if self.dir_new_path.exists() and not globals.force: # base may exist, but nothing else
			assert index == (), index
		else: self.dir_new_path.mkdir()
		self.dir_diff_ropath = ropath

	def end_process(self):
		"""Update information of a directory when leaving it"""
		if self.dir_diff_ropath:
			self.dir_diff_ropath.copy_attribs(self.dir_new_path)

	def can_fast_process(self, index, ropath):
		"""Can fast process (no recursion) if ropath isn't a directory"""
		log.Log("Writing %s of type %s" %
				(ropath.get_relative_path(), ropath.type), 5)
		return not ropath.isdir()

	def fast_process(self, index, ropath):
		"""Write non-directory ropath to destination"""
		if ropath.exists(): ropath.copy(self.base_path.new_index(index))


