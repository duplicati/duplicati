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

"""Generate and process backup statistics"""

import re, time
import lazy, dup_time, os


class StatsException(Exception): pass

class StatsObj:
	"""Contains various statistics, provide string conversion functions"""
	# used when quoting files in get_stats_line
	space_regex = re.compile(" ")

	stat_file_attrs = ('SourceFiles', 'SourceFileSize',
					   'NewFiles', 'NewFileSize',
					   'DeletedFiles',
					   'ChangedFiles', 'ChangedFileSize', 'ChangedDeltaSize',
					   'DeltaEntries', 'RawDeltaSize')
	stat_misc_attrs = ('Errors', 'TotalDestinationSizeChange')
	stat_time_attrs = ('StartTime', 'EndTime', 'ElapsedTime')
	stat_attrs = (('Filename',) + stat_time_attrs +
				  stat_misc_attrs + stat_file_attrs)

	# Below, the second value in each pair is true iff the value
	# indicates a number of bytes
	stat_file_pairs = (('SourceFiles', None), ('SourceFileSize', 1),
					   ('NewFiles', None), ('NewFileSize', 1),
					   ('DeletedFiles', None),
					   ('ChangedFiles', None), ('ChangedFileSize', 1),
					   ('ChangedDeltaSize', 1),
					   ('DeltaEntries', None), ('RawDeltaSize', 1))

	# This is used in get_byte_summary_string below
	byte_abbrev_list = ((1024*1024*1024*1024, "TB"),
						(1024*1024*1024, "GB"),
						(1024*1024, "MB"),
						(1024, "KB"))

	def __init__(self):
		"""Set attributes to None"""
		for attr in self.stat_attrs: self.__dict__[attr] = None

	def get_stat(self, attribute):
		"""Get a statistic"""
		return self.__dict__[attribute]

	def set_stat(self, attr, value):
		"""Set attribute to given value"""
		self.__dict__[attr] = value

	def increment_stat(self, attr):
		"""Add 1 to value of attribute"""
		self.__dict__[attr] += 1

	def get_total_dest_size_change(self):
		"""Return total destination size change

		This represents the total increase in the size of the
		duplicity destination directory, or None if not available.

		"""
		return 0 # this needs to be re-done for duplicity

	def get_stats_line(self, index, use_repr = 1):
		"""Return one line abbreviated version of full stats string"""
		file_attrs = map(lambda attr: str(self.get_stat(attr)),
						 self.stat_file_attrs)
		if not index: filename = "."
		else:
			filename = apply(os.path.join, index)
			if use_repr:
				# use repr to quote newlines in relative filename, then
				# take of leading and trailing quote and quote spaces.
				filename = self.space_regex.sub("\\x20", repr(filename)[1:-1])
		return " ".join([filename,] + file_attrs)

	def set_stats_from_line(self, line):
		"""Set statistics from given line"""
		def error(): raise StatsException("Bad line '%s'" % line)
		if line[-1] == "\n": line = line[:-1]
		lineparts = line.split(" ")
		if len(lineparts) < len(stat_file_attrs): error()
		for attr, val_string in zip(stat_file_attrs,
									lineparts[-len(stat_file_attrs):]):
			try: val = long(val_string)
			except ValueError:
				try: val = float(val_string)
				except ValueError: error()
			self.set_stat(attr, val)
		return self

	def get_stats_string(self):
		"""Return extended string printing out statistics"""
		return "%s%s%s" % (self.get_timestats_string(),
						   self.get_filestats_string(),
						   self.get_miscstats_string())

	def get_timestats_string(self):
		"""Return portion of statistics string dealing with time"""
		timelist = []
		if self.StartTime is not None:
			timelist.append("StartTime %.2f (%s)\n" %
					 (self.StartTime, dup_time.timetopretty(self.StartTime)))
		if self.EndTime is not None:
			timelist.append("EndTime %.2f (%s)\n" %
						 (self.EndTime, dup_time.timetopretty(self.EndTime)))
		if self.ElapsedTime or (self.StartTime is not None and
								self.EndTime is not None):
			if self.ElapsedTime is None:
				self.ElapsedTime = self.EndTime - self.StartTime
			timelist.append("ElapsedTime %.2f (%s)\n" %
				 (self.ElapsedTime, dup_time.inttopretty(self.ElapsedTime)))
		return "".join(timelist)

	def get_filestats_string(self):
		"""Return portion of statistics string about files and bytes"""
		def fileline(stat_file_pair):
			"""Return zero or one line of the string"""
			attr, in_bytes = stat_file_pair
			val = self.get_stat(attr)
			if val is None: return ""
			if in_bytes:
				return "%s %s (%s)\n" % (attr, val,
										 self.get_byte_summary_string(val))
			else: return "%s %s\n" % (attr, val)

		return "".join(map(fileline, self.stat_file_pairs))

	def get_miscstats_string(self):
		"""Return portion of extended stat string about misc attributes"""
		misc_string = ""
		tdsc = self.TotalDestinationSizeChange
		if tdsc is not None:
			misc_string += ("TotalDestinationSizeChange %s (%s)\n" %
							(tdsc, self.get_byte_summary_string(tdsc)))
		if self.Errors is not None: misc_string += "Errors %d\n" % self.Errors
		return misc_string

	def get_byte_summary_string(self, byte_count):
		"""Turn byte count into human readable string like "7.23GB" """
		if byte_count < 0:
			sign = "-"
			byte_count = -byte_count
		else: sign = ""

		for abbrev_bytes, abbrev_string in self.byte_abbrev_list:
			if byte_count >= abbrev_bytes:
				# Now get 3 significant figures
				abbrev_count = float(byte_count)/abbrev_bytes
				if abbrev_count >= 100: precision = 0
				elif abbrev_count >= 10: precision = 1
				else: precision = 2
				return "%s%%.%df %s" % (sign, precision, abbrev_string) \
					   % (abbrev_count,)
		byte_count = round(byte_count)
		if byte_count == 1: return sign + "1 byte"
		else: return "%s%d bytes" % (sign, byte_count)

	def get_stats_logstring(self, title):
		"""Like get_stats_string, but add header and footer"""
		header = "--------------[ %s ]--------------" % title
		footer = "-" * len(header)
		return "%s\n%s%s\n" % (header, self.get_stats_string(), footer)

	def set_stats_from_string(self, s):
		"""Initialize attributes from string, return self for convenience"""
		def error(line): raise StatsException("Bad line '%s'" % line)

		for line in s.split("\n"):
			if not line: continue
			line_parts = line.split()
			if len(line_parts) < 2: error(line)
			attr, value_string = line_parts[:2]
			if not attr in self.stat_attrs: error(line)
			try:
				try: val1 = long(value_string)
				except ValueError: val1 = None
				val2 = float(value_string)
				if val1 == val2: self.set_stat(attr, val1) # use integer val
				else: self.set_stat(attr, val2) # use float
			except ValueError: error(line)
		return self

	def write_stats_to_path(self, path):
		"""Write statistics string to given path"""
		fin = path.open("w")
		fin.write(self.get_stats_string())
		assert not fin.close()

	def read_stats_from_path(self, path):
		"""Set statistics from path, return self for convenience"""
		fp = path.open("r")
		self.set_stats_from_string(fp.read())
		assert not fp.close()
		return self

	def stats_equal(self, s):
		"""Return true if s has same statistics as self"""
		assert isinstance(s, StatsObj)
		for attr in self.stat_file_attrs:
			if self.get_stat(attr) != s.get_stat(attr): return None
		return 1

	def set_to_average(self, statobj_list):
		"""Set self's attributes to average of those in statobj_list"""
		for attr in self.stat_attrs: self.set_stat(attr, 0)
		for statobj in statobj_list:
			for attr in self.stat_attrs:
				if statobj.get_stat(attr) is None: self.set_stat(attr, None)
				elif self.get_stat(attr) is not None:
					self.set_stat(attr, statobj.get_stat(attr) +
								  self.get_stat(attr))

		# Don't compute average starting/stopping time		
		self.StartTime = None
		self.EndTime = None

		for attr in self.stat_attrs:
			if self.get_stat(attr) is not None:
				self.set_stat(attr,
							  self.get_stat(attr)/float(len(statobj_list)))
		return self

	def get_statsobj_copy(self):
		"""Return new StatsObj object with same stats as self"""
		s = StatObj()
		for attr in self.stat_attrs: s.set_stat(attr, self.get_stat(attr))
		return s


class StatsDeltaProcess(StatsObj):
	"""Keep track of statistics during DirDelta process"""
	def __init__(self):
		"""StatsDeltaProcess initializer - zero file attributes"""
		StatsObj.__init__(self)
		for attr in StatsObj.stat_file_attrs: self.__dict__[attr] = 0
		self.Errors = 0
		self.StartTime = time.time()

	def add_new_file(self, path):
		"""Add stats of new file path to statistics"""
		filesize = path.getsize()
		self.SourceFiles += 1
		self.SourceFileSize += filesize
		self.NewFiles += 1
		self.NewFileSize += filesize
		self.DeltaEntries += 1

	def add_changed_file(self, path):
		"""Add stats of file that has changed since last backup"""
		filesize = path.getsize()
		self.SourceFiles += 1
		self.SourceFileSize += filesize
		self.ChangedFiles += 1
		self.ChangedFileSize += filesize
		self.DeltaEntries += 1

	def add_deleted_file(self):
		"""Add stats of file no longer in source directory"""
		self.DeletedFiles += 1 # can't add size since not available
		self.DeltaEntries += 1

	def add_unchanged_file(self, path):
		"""Add stats of file that hasn't changed since last backup"""
		filesize = path.getsize()
		self.SourceFiles += 1
		self.SourceFileSize += filesize

	def close(self):
		"""End collection of data, set EndTime"""
		self.EndTime = time.time()

