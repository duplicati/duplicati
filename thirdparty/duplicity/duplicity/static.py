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

"""MakeStatic and MakeClass

These functions are used to make all the instance methods in a class
into static or class methods.

"""

class StaticMethodsError(Exception): pass

def MakeStatic(cls):
	"""turn instance methods into static ones

	The methods (that don't begin with _) of any class that
	subclasses this will be turned into static methods.

	"""
	for name in dir(cls):
		if name[0] != "_":
			cls.__dict__[name] = staticmethod(cls.__dict__[name])

def MakeClass(cls):
	"""Turn instance methods into classmethods.  Ignore _ like above"""
	for name in dir(cls):
		if name[0] != "_":
			cls.__dict__[name] = classmethod(cls.__dict__[name])
