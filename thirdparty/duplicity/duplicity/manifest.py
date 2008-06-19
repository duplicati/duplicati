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

"""Create and edit manifest for session contents"""

import re
import log, globals

class ManifestError(Exception):
	"""Exception raised when problem with manifest"""
	pass

class Manifest:
	"""List of volumes and information about each one"""
	def __init__(self):
		"""Create blank Manifest"""
		self.hostname = None; self.local_dirname = None
		self.volume_info_dict = {} # dictionary vol numbers -> vol infos

	def set_dirinfo(self):
		"""Set information about directory from globals"""
		self.hostname = globals.hostname
		self.local_dirname = globals.local_path.name
		return self

	def check_dirinfo(self):
		"""Return None if dirinfo is the same, otherwise error message

		Does not raise an error message if hostname or local_dirname
		are not available.

		"""
		if globals.allow_source_mismatch: return
		if self.hostname and self.hostname != globals.hostname:
			errmsg = """Fatal Error: Backup source host has changed.
Current hostname: %s
Previous hostname: %s""" % (globals.hostname, self.hostname)
		elif (self.local_dirname and
			  self.local_dirname != globals.local_path.name):
			errmsg = """Fatal Error: Backup source directory has changed.
Current directory: %s
Previous directory: %s""" % (self.local_dirname, globals.local_path.name)
		else: return

		log.FatalError(errmsg + """

Aborting because you may have accidentally tried to backup two
different data sets to the same remote location, or using the same
archive directory.  If this is not a mistake, use the
--allow-source-mismatch switch to avoid seeing this message""")

	def add_volume_info(self, vi):
		"""Add volume info vi to manifest"""
		vol_num = vi.volume_number
		if self.volume_info_dict.has_key(vol_num):
			raise ManifestError("Volume %d already present" % (vol_num,))
		self.volume_info_dict[vol_num] = vi

	def to_string(self):
		"""Return string version of self (just concatenate vi strings)"""
		result = ""
		if self.hostname: result += "Hostname %s\n" % self.hostname
		if self.local_dirname:
			result += "Localdir %s\n" % Quote(self.local_dirname)

		vol_num_list = self.volume_info_dict.keys()
		vol_num_list.sort()
		def vol_num_to_string(vol_num):
			return self.volume_info_dict[vol_num].to_string()
		result = "%s%s\n" % (result,
							 "\n".join(map(vol_num_to_string, vol_num_list)))
		return result
	__str__ = to_string

	def from_string(self, s):
		"""Initialize self from string s, return self"""
		def get_field(fieldname):
			"""Return the value of a field by parsing s, or None if no field"""
			m = re.search("(^|\\n)%s\\s(.*?)\n" % fieldname, s, re.I)
			if not m: return None
			else: return Unquote(m.group(2))
		self.hostname = get_field("hostname")
		self.local_dirname = get_field("localdir")

		next_vi_string_regexp = re.compile("(^|\\n)(volume\\s.*?)"
										   "(\\nvolume\\s|$)", re.I | re.S)
		starting_s_index = 0
		while 1:
			match = next_vi_string_regexp.search(s[starting_s_index:])
			if not match: break
			self.add_volume_info(VolumeInfo().from_string(match.group(2)))
			starting_s_index += match.end(2)
		return self

	def __eq__(self, other):
		"""Two manifests are equal if they contain the same volume infos"""
		vi_list1 = self.volume_info_dict.keys()
		vi_list1.sort()
		vi_list2 = other.volume_info_dict.keys()
		vi_list2.sort()
		if vi_list1 != vi_list2:
			log.Log("Manifests not equal because different volume numbers", 3)
			return None
		for i in range(len(vi_list1)):
			if not vi_list1[i] == vi_list2[i]: return None

		if (self.hostname != other.hostname or
			self.local_dirname != other.local_dirname): return None
		return 1

	def __ne__(self, other):
		"""Defines !=.  Not doing this always leads to annoying bugs..."""
		return not self.__eq__(other)

	def write_to_path(self, path):
		"""Write string version of manifest to given path"""
		assert not path.exists()
		fout = path.open("w")
		fout.write(self.to_string())
		assert not fout.close()
		path.setdata()

	def get_containing_volumes(self, index_prefix):
		"""Return list of volume numbers that may contain index_prefix"""
		return filter(lambda vol_num:
					   self.volume_info_dict[vol_num].contains(index_prefix),
					  self.volume_info_dict.keys())


class VolumeInfoError(Exception):
	"""Raised when there is a problem initializing a VolumeInfo from string"""
	pass

class VolumeInfo:
	"""Information about a single volume"""
	def __init__(self):
		"""VolumeInfo initializer"""
		self.volume_number = None
		self.start_index, self.end_index = None, None
		self.hashes = {}

	def set_info(self, vol_number, start_index, end_index):
		"""Set essential VolumeInfo information, return self

		Call with starting and ending paths stored in the volume.  If
		a multivol diff gets split between volumes, count it as being
		part of both volumes.

		"""
		self.volume_number = vol_number
		self.start_index, self.end_index = start_index, end_index
		return self

	def set_hash(self, hash_name, data):
		"""Set the value of hash hash_name (e.g. "MD5") to data"""
		self.hashes[hash_name] = data

	def get_best_hash(self):
		"""Return pair (hash_type, hash_data)

		SHA1 is the best hash, and MD5 is the second best hash.  None
		is returned if no hash is available.

		"""
		if not self.hashes: return None
		try: return ("SHA1", self.hashes['SHA1'])
		except KeyError: pass
		try: return ("MD5", self.hashes['MD5'])
		except KeyError: pass
		return self.hashes.items()[0]

	def to_string(self):
		"""Return nicely formatted string reporting all information"""
		def index_to_string(index):
			"""Return printable version of index without any whitespace"""
			if index:
				s = "/".join(index)
				return Quote(s)
			else: return "."

		slist = ["Volume %d:" % self.volume_number]
		whitespace = "    "
		slist.append("%sStartingPath   %s" %
					 (whitespace, index_to_string(self.start_index)))
		slist.append("%sEndingPath     %s" %
					 (whitespace, index_to_string(self.end_index)))
		for key in self.hashes:
			slist.append("%sHash %s %s" %
						 (whitespace, key, self.hashes[key]))
		return "\n".join(slist)
	__str__ = to_string

	def from_string(self, s):
		"""Initialize self from string s as created by to_string"""
		def string_to_index(s):
			"""Return tuple index from string"""
			s = Unquote(s)
			if s == ".": return ()
			return tuple(s.split("/"))

		linelist = s.strip().split("\n")

		# Set volume number
		m = re.search("^Volume ([0-9]+):", linelist[0], re.I)
		if not m: raise VolumeInfoError("Bad first line '%s'" % (linelist[0],))
		self.volume_number = int(m.group(1))

		# Set other fields
		for line in linelist[1:]:
			if not line: continue
			line_split = line.strip().split()
			field_name = line_split[0].lower()
			other_fields = line_split[1:]
			if field_name == "Volume":
				log.Log("Warning, found extra Volume identifier", 2)
				break
			elif field_name == "startingpath":
				self.start_index = string_to_index(other_fields[0])
			elif field_name == "endingpath":
				self.end_index = string_to_index(other_fields[0])
			elif field_name == "hash":
				self.set_hash(other_fields[0], other_fields[1])

		if self.start_index is None or self.end_index is None:
			raise VolumeInfoError("Start or end index not set")
		return self

	def __eq__(self, other):
		"""Used in test suite"""
		if not isinstance(other, VolumeInfo):
			log.Log("Other is not VolumeInfo", 3)
			return None
		if self.volume_number != other.volume_number:
			log.Log("Volume numbers don't match", 3)
			return None
		if self.start_index != other.start_index:
			log.Log("start_indicies don't match", 3)
			return None
		if self.end_index != other.end_index:
			log.Log("end_index don't match", 3)
			return None
		hash_list1 = self.hashes.items()
		hash_list1.sort()
		hash_list2 = other.hashes.items()
		hash_list2.sort()
		if hash_list1 != hash_list2:
			log.Log("Hashes don't match", 3)
			return None
		return 1

	def __ne__(self, other):
		"""Defines !="""
		return not self.__eq__(other)

	def contains(self, index_prefix, recursive = 1):
		"""Return true if volume might contain index

		If recursive is true, then return true if any index starting
		with index_prefix could be contained.  Otherwise, just check
		if index_prefix itself is between starting and ending
		indicies.

		"""
		if recursive: return (self.start_index[:len(index_prefix)] <=
							  index_prefix <= self.end_index)
		else: return self.start_index <= index_prefix <= self.end_index


nonnormal_char_re = re.compile("(\\s|[\\\\\"'])")
def Quote(s):
	"""Return quoted version of s safe to put in a manifest or volume info"""
	if not nonnormal_char_re.search(s): return s # no quoting necessary
	slist = []
	for char in s:
		if nonnormal_char_re.search(char):
			slist.append("\\x%02x" % ord(char))
		else: slist.append(char)
	return '"%s"' % "".join(slist)

def Unquote(quoted_string):
	"""Return original string from quoted_string produced by above"""
	if not quoted_string[0] == '"' or quoted_string[0] == "'":
		return quoted_string
	assert quoted_string[0] == quoted_string[-1]
	return_list = []
	i = 1 # skip initial char
	while i < len(quoted_string)-1:
		char = quoted_string[i]
		if char == "\\": # quoted section
			assert quoted_string[i+1] == "x"
			return_list.append(chr(int(quoted_string[i+2:i+4], 16)))
			i += 4
		else:
			return_list.append(char)
			i += 1
	return "".join(return_list)
