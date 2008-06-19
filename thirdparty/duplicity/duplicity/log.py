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

"""Log various messages depending on verbosity level"""

import sys

verbosity = 3
termverbosity = 3

def Log(s, verb_level):
	"""Write s to stderr if verbosity level low enough"""
	if verb_level <= termverbosity:
		if verb_level <= 2:
			sys.stderr.write(s + "\n")
			sys.stderr.flush()
		else:
			sys.stdout.write(s + "\n")
			sys.stdout.flush()

def Debug(s):
	"""Shortcut used for debug message (verbosity 9)."""
	Log(s, 9)

def Info(s):
	"""Shortcut used for info messages (verbosity 5)."""
	Log(s, 5)

def Notice(s):
	"""Shortcut used for notice messages (verbosity 3, the default)."""
	Log(s, 3)

def Warn(s):
	"""Shortcut used for warning messages (verbosity 2)"""
	Log(s, 2)

def FatalError(s):
	"""Write fatal error message and exit"""
	sys.stderr.write(s + "\n")
	sys.stderr.flush()
	sys.exit(1)

def setverbosity(verb, termverb = None):
	"""Set the verbosity level"""
	global verbosity, termverbosity
	verbosity = verb
	if termverb: termverbosity = termverb
	else: termverbosity = verb
