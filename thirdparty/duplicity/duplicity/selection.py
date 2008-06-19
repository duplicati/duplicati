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

"""Iterate exactly the requested files in a directory

Parses includes and excludes to yield correct files.  More
documentation on what this code does can be found on the man page.

"""

from __future__ import generators
import re
from path import *
import robust, log, globals
import os, stat


class SelectError(Exception):
	"""Some error dealing with the Select class"""
	pass

class FilePrefixError(SelectError):
	"""Signals that a specified file doesn't start with correct prefix"""
	pass

class GlobbingError(SelectError):
	"""Something has gone wrong when parsing a glob string"""
	pass


class Select:
	"""Iterate appropriate Paths in given directory

	This class acts as an iterator on account of its next() method.
	Basically, it just goes through all the files in a directory in
	order (depth-first) and subjects each file to a bunch of tests
	(selection functions) in order.  The first test that includes or
	excludes the file means that the file gets included (iterated) or
	excluded.  The default is include, so with no tests we would just
	iterate all the files in the directory in order.

	The one complication to this is that sometimes we don't know
	whether or not to include a directory until we examine its
	contents.  For instance, if we want to include all the **.py
	files.  If /home/ben/foo.py exists, we should also include /home
	and /home/ben, but if these directories contain no **.py files,
	they shouldn't be included.  For this reason, a test may not
	include or exclude a directory, but merely "scan" it.  If later a
	file in the directory gets included, so does the directory.

	As mentioned above, each test takes the form of a selection
	function.  The selection function takes a path, and returns:

	None - means the test has nothing to say about the related file
	0 - the file is excluded by the test
	1 - the file is included
	2 - the test says the file (must be directory) should be scanned

	Also, a selection function f has a variable f.exclude which should
	be true iff f could potentially exclude some file.  This is used
	to signal an error if the last function only includes, which would
	be redundant and presumably isn't what the user intends.

	"""
	# This re should not match normal filenames, but usually just globs
	glob_re = re.compile("(.*[*?[]|ignorecase\\:)", re.I | re.S)

	def __init__(self, path):
		"""Initializer, called with Path of root directory"""
		assert isinstance(path, Path), str(path)
		self.selection_functions = []
		self.rootpath = path
		self.prefix = self.rootpath.name

	def set_iter(self):
		"""Initialize generator, prepare to iterate."""
		self.rootpath.setdata() # this may have changed since Select init
		self.iter = self.Iterate(self.rootpath)
		self.next = self.iter.next
		self.__iter__ = lambda: self
		return self

	def Iterate(self, path):
		"""Return iterator yielding paths in path

		This function looks a bit more complicated than it needs to be
		because it avoids extra recursion (and no extra function calls
		for non-directory files) while still doing the "directory
		scanning" bit.

		"""
		def error_handler(exc, path, filename):
			try:
				mode = os.stat(path.name+"/"+filename)[stat.ST_MODE]
				if stat.S_ISSOCK(mode):
					log.Log("Skipping socket %s/%s" % (path.name, filename), 7)
				else:
					log.Log("Error initializing file %s/%s" % (path.name, filename), 2)
			except OSError:
				log.Log("Error accessing possibly locked file %s/%s" % (path.name, filename), 2);
			return None

		def diryield(path):
			"""Generate relevant files in directory path

			Returns (path, num) where num == 0 means path should be
			generated normally, num == 1 means the path is a directory
			and should be included iff something inside is included.

			"""
			for filename in robust.listpath(path):
				new_path = robust.check_common_error(
					error_handler, Path.append, (path, filename))
				if new_path:
					s = self.Select(new_path)
					if s == 1: yield (new_path, 0)
					elif s == 2 and new_path.isdir(): yield (new_path, 1)

		if not path.type: # base doesn't exist
			log.Log("Warning: base %s doesn't exist, continuing" %
					path.name, 2)
			return
		log.Log("Selecting %s" % path.name, 7)
		yield path
		if not path.isdir(): return
		diryield_stack = [diryield(path)]
		delayed_path_stack = []

		while diryield_stack:
			try: subpath, val = diryield_stack[-1].next()
			except StopIteration:
				diryield_stack.pop()
				if delayed_path_stack: delayed_path_stack.pop()
				continue
			if val == 0:
				if delayed_path_stack:
					for delayed_path in delayed_path_stack:
						log.Log("Selecting %s" % delayed_path.name, 7)
						yield delayed_path
					del delayed_path_stack[:]
				log.Log("Selecting %s" % subpath.name, 7)
				yield subpath
				if subpath.isdir(): diryield_stack.append(diryield(subpath))
			elif val == 1:
				delayed_path_stack.append(subpath)
				diryield_stack.append(diryield(subpath))

	def Select(self, path):
		"""Run through the selection functions and return dominant val 0/1/2"""
		for sf in self.selection_functions:
			result = sf(path)
			if result is not None: return result
		return 1

	def ParseArgs(self, argtuples, filelists):
		"""Create selection functions based on list of tuples

		The tuples are created when the initial commandline arguments
		are read.  They have the form (option string, additional
		argument) except for the filelist tuples, which should be
		(option-string, (additional argument, filelist_fp)).

		"""
		filelists_index = 0
		try:
			for opt, arg in argtuples:
				if opt == "--exclude":
					self.add_selection_func(self.glob_get_sf(arg, 0))
				elif opt == "--exclude-device-files":
					self.add_selection_func(self.devfiles_get_sf())
				elif opt == "--exclude-filelist":
					self.add_selection_func(self.filelist_get_sf(
						filelists[filelists_index], 0, arg))
					filelists_index += 1
				elif opt == "--exclude-globbing-filelist":
					map(self.add_selection_func,
						self.filelist_globbing_get_sfs(filelists[filelists_index], 0, arg))
					filelists_index += 1
				elif opt == "--exclude-other-filesystems":
					self.add_selection_func(self.other_filesystems_get_sf(0))
				elif opt == "--exclude-regexp":
					self.add_selection_func(self.regexp_get_sf(arg, 0))
				elif opt == "--include":
					self.add_selection_func(self.glob_get_sf(arg, 1))
				elif opt == "--include-filelist":
					self.add_selection_func(self.filelist_get_sf(
						filelists[filelists_index], 1, arg))
					filelists_index += 1
				elif opt == "--include-globbing-filelist":
					map(self.add_selection_func,
						self.filelist_globbing_get_sfs(filelists[filelists_index], 1, arg))
					filelists_index += 1
				elif opt == "--include-regexp":
					self.add_selection_func(self.regexp_get_sf(arg, 1))
				else: assert 0, "Bad selection option %s" % opt
		except SelectError, e: self.parse_catch_error(e)
		assert filelists_index == len(filelists)
		self.parse_last_excludes()

	def parse_catch_error(self, exc):
		"""Deal with selection error exc"""
		if isinstance(exc, FilePrefixError):
			log.FatalError(
"""Fatal Error: The file specification
    %s
cannot match any files in the base directory
    %s
Useful file specifications begin with the base directory or some
pattern (such as '**') which matches the base directory.""" %
			(exc, self.prefix))
		elif isinstance(e, GlobbingError):
			log.FatalError("Fatal Error while processing expression\n"
						   "%s" % exc)
		else: raise

	def parse_last_excludes(self):
		"""Exit with error if last selection function isn't an exclude"""
		if (self.selection_functions and
			not self.selection_functions[-1].exclude):
			log.FatalError(
"""Last selection expression:
    %s
only specifies that files be included.  Because the default is to
include all files, the expression is redundant.  Exiting because this
probably isn't what you meant.""" %
			(self.selection_functions[-1].name,))

	def add_selection_func(self, sel_func, add_to_start = None):
		"""Add another selection function at the end or beginning"""
		if add_to_start: self.selection_functions.insert(0, sel_func)
		else: self.selection_functions.append(sel_func)

	def filelist_get_sf(self, filelist_fp, inc_default, filelist_name):
		"""Return selection function by reading list of files

		The format of the filelist is documented in the man page.
		filelist_fp should be an (open) file object.
		inc_default should be true if this is an include list,
		false for an exclude list.
		filelist_name is just a string used for logging.

		"""
		log.Log("Reading filelist %s" % filelist_name, 4)
		tuple_list, something_excluded = \
					self.filelist_read(filelist_fp, inc_default, filelist_name)
		log.Log("Sorting filelist %s" % filelist_name, 4)
		tuple_list.sort()
		i = [0] # We have to put index in list because of stupid scoping rules

		def selection_function(path):
			while 1:
				if i[0] >= len(tuple_list): return None
				include, move_on = \
						 self.filelist_pair_match(path, tuple_list[i[0]])
				if move_on:
					i[0] += 1
					if include is None: continue # later line may match
				return include

		selection_function.exclude = something_excluded or inc_default == 0
		selection_function.name = "Filelist: " + filelist_name
		return selection_function

	def filelist_read(self, filelist_fp, include, filelist_name):
		"""Read filelist from fp, return (tuplelist, something_excluded)"""
		prefix_warnings = [0]
		def incr_warnings(exc):
			"""Warn if prefix is incorrect"""
			prefix_warnings[0] += 1
			if prefix_warnings[0] < 6:
				log.Log("Warning: file specification '%s' in filelist %s\n"
						"doesn't start with correct prefix %s.  Ignoring." %
						(exc, filelist_name, self.prefix), 2)
				if prefix_warnings[0] == 5:
					log.Log("Future prefix errors will not be logged.", 2)

		something_excluded, tuple_list = None, []
		separator = globals.null_separator and "\0" or "\n"
		for line in filelist_fp.read().split(separator):
			if not line: continue # skip blanks
			try: tuple = self.filelist_parse_line(line, include)
			except FilePrefixError, exc:
				incr_warnings(exc)
				continue
			tuple_list.append(tuple)
			if not tuple[1]: something_excluded = 1
		if filelist_fp.close():
			log.Log("Error closing filelist %s" % filelist_name, 2)
		return (tuple_list, something_excluded)

	def filelist_parse_line(self, line, include):
		"""Parse a single line of a filelist, returning a pair

		pair will be of form (index, include), where index is another
		tuple, and include is 1 if the line specifies that we are
		including a file.  The default is given as an argument.
		prefix is the string that the index is relative to.

		"""
		line = line.strip()
		if line[:2] == "+ ": # Check for "+ "/"- " syntax
			include = 1
			line = line[2:]
		elif line[:2] == "- ":
			include = 0
			line = line[2:]

		if not line.startswith(self.prefix): raise FilePrefixError(line)
		line = line[len(self.prefix):] # Discard prefix
		index = tuple(filter(lambda x: x, line.split("/"))) # remove empties
		return (index, include)

	def filelist_pair_match(self, path, pair):
		"""Matches a filelist tuple against a path

		Returns a pair (include, move_on).  include is None if the
		tuple doesn't match either way, and 0/1 if the tuple excludes
		or includes the path.

		move_on is true if the tuple cannot match a later index, and
		so we should move on to the next tuple in the index.

		"""
		index, include = pair
		if include == 1:
			if index < path.index: return (None, 1)
			if index == path.index: return (1, 1)
			elif index[:len(path.index)] == path.index:
				return (1, None) # /foo/bar implicitly includes /foo
			else: return (None, None) # path greater, not initial sequence
		elif include == 0:
			if path.index[:len(index)] == index:
				return (0, None) # /foo implicitly excludes /foo/bar
			elif index < path.index: return (None, 1)
			else: return (None, None) # path greater, not initial sequence
		else: assert 0, "Include is %s, should be 0 or 1" % (include,)

	def filelist_globbing_get_sfs(self, filelist_fp, inc_default, list_name):
		"""Return list of selection functions by reading fileobj

		filelist_fp should be an open file object
		inc_default is true iff this is an include list
		list_name is just the name of the list, used for logging
		See the man page on --[include/exclude]-globbing-filelist

		"""
		log.Log("Reading globbing filelist %s" % list_name, 4)
		separator = globals.null_separator and "\0" or "\n"
		for line in filelist_fp.read().split(separator):
			if not line: continue # skip blanks
			if line[:2] == "+ ": yield self.glob_get_sf(line[2:], 1)
			elif line[:2] == "- ": yield self.glob_get_sf(line[2:], 0)
			else: yield self.glob_get_sf(line, inc_default)

	def other_filesystems_get_sf(self, include):
		"""Return selection function matching files on other filesystems"""
		assert include == 0 or include == 1
		root_devloc = self.rootpath.getdevloc()
		def sel_func(path):
			if path.exists() and path.getdevloc() != root_devloc:
				return include
			else: return None
		sel_func.exclude = not include
		sel_func.name = "Match other filesystems"
		return sel_func

	def regexp_get_sf(self, regexp_string, include):
		"""Return selection function given by regexp_string"""
		assert include == 0 or include == 1
		try: regexp = re.compile(regexp_string)
		except:
			log.Log("Error compiling regular expression %s" % regexp_string, 1)
			raise
		
		def sel_func(path):
			if regexp.search(path.name): return include
			else: return None

		sel_func.exclude = not include
		sel_func.name = "Regular expression: %s" % regexp_string
		return sel_func

	def devfiles_get_sf(self):
		"""Return a selection function to exclude all dev files"""
		if self.selection_functions:
			log.Log("Warning: exclude-device-files is not the first "
					"selector.\nThis may not be what you intended", 3)
		def sel_func(path):
			if path.isdev(): return 0
			else: return None
		sel_func.exclude = 1
		sel_func.name = "Exclude device files"
		return sel_func

	def glob_get_sf(self, glob_str, include):
		"""Return selection function given by glob string"""
		assert include == 0 or include == 1
		if glob_str == "**": sel_func = lambda path: include
		elif not self.glob_re.match(glob_str): # normal file
			sel_func = self.glob_get_filename_sf(glob_str, include)
		else: sel_func = self.glob_get_normal_sf(glob_str, include)

		sel_func.exclude = not include
		sel_func.name = "Command-line %s glob: %s" % \
						(include and "include" or "exclude", glob_str)
		return sel_func

	def glob_get_filename_sf(self, filename, include):
		"""Get a selection function given a normal filename

		Some of the parsing is better explained in
		filelist_parse_line.  The reason this is split from normal
		globbing is things are a lot less complicated if no special
		globbing characters are used.

		"""
		if not filename.startswith(self.prefix):
			raise FilePrefixError(filename)
		index = tuple(filter(lambda x: x,
							 filename[len(self.prefix):].split("/")))
		return self.glob_get_tuple_sf(index, include)

	def glob_get_tuple_sf(self, tuple, include):
		"""Return selection function based on tuple"""
		def include_sel_func(path):
			if (path.index == tuple[:len(path.index)] or
				path.index[:len(tuple)] == tuple):
				return 1 # /foo/bar implicitly matches /foo, vice-versa
			else: return None

		def exclude_sel_func(path):
			if path.index[:len(tuple)] == tuple:
				return 0 # /foo excludes /foo/bar, not vice-versa
			else: return None

		if include == 1: sel_func = include_sel_func
		elif include == 0: sel_func = exclude_sel_func
		sel_func.exclude = not include
		sel_func.name = "Tuple select %s" % (tuple,)
		return sel_func

	def glob_get_normal_sf(self, glob_str, include):
		"""Return selection function based on glob_str

		The basic idea is to turn glob_str into a regular expression,
		and just use the normal regular expression.  There is a
		complication because the selection function should return '2'
		(scan) for directories which may contain a file which matches
		the glob_str.  So we break up the glob string into parts, and
		any file which matches an initial sequence of glob parts gets
		scanned.

		Thanks to Donovan Baarda who provided some code which did some
		things similar to this.

		"""
		if glob_str.lower().startswith("ignorecase:"):
			re_comp = lambda r: re.compile(r, re.I | re.S)
			glob_str = glob_str[len("ignorecase:"):]
		else: re_comp = lambda r: re.compile(r, re.S)

		# matches what glob matches and any files in directory
		glob_comp_re = re_comp("^%s($|/)" % self.glob_to_re(glob_str))

		if glob_str.find("**") != -1:
			glob_str = glob_str[:glob_str.find("**")+2] # truncate after **

		scan_comp_re = re_comp("^(%s)$" %
							   "|".join(self.glob_get_prefix_res(glob_str)))

		def include_sel_func(path):
			if glob_comp_re.match(path.name): return 1
			elif scan_comp_re.match(path.name): return 2
			else: return None

		def exclude_sel_func(path):
			if glob_comp_re.match(path.name): return 0
			else: return None

		# Check to make sure prefix is ok
		if not include_sel_func(self.rootpath): raise FilePrefixError(glob_str)
		
		if include: return include_sel_func
		else: return exclude_sel_func

	def glob_get_prefix_res(self, glob_str):
		"""Return list of regexps equivalent to prefixes of glob_str"""
		glob_parts = glob_str.split("/")
		if "" in glob_parts[1:-1]: # "" OK if comes first or last, as in /foo/
			raise GlobbingError("Consecutive '/'s found in globbing string "
								+ glob_str)

		prefixes = map(lambda i: "/".join(glob_parts[:i+1]),
					   range(len(glob_parts)))
		# we must make exception for root "/", only dir to end in slash
		if prefixes[0] == "": prefixes[0] = "/"
		return map(self.glob_to_re, prefixes)

	def glob_to_re(self, pat):
		"""Returned regular expression equivalent to shell glob pat

		Currently only the ?, *, [], and ** expressions are supported.
		Ranges like [a-z] are also currently unsupported.  There is no
		way to quote these special characters.

		This function taken with minor modifications from efnmatch.py
		by Donovan Baarda.

		"""
		i, n, res = 0, len(pat), ''
		while i < n:
			c, s = pat[i], pat[i:i+2]
			i = i+1
			if s == '**':
				res = res + '.*'
				i = i + 1
			elif c == '*': res = res + '[^/]*'
			elif c == '?': res = res + '[^/]'
			elif c == '[':
				j = i
				if j < n and pat[j] in '!^': j = j+1
				if j < n and pat[j] == ']': j = j+1
				while j < n and pat[j] != ']': j = j+1
				if j >= n: res = res + '\\[' # interpret the [ literally
				else: # Deal with inside of [..]
					stuff = pat[i:j].replace('\\','\\\\')
					i = j+1
					if stuff[0] in '!^': stuff = '^' + stuff[1:]
					res = res + '[' + stuff + ']'
			else: res = res + re.escape(c)
		return res


