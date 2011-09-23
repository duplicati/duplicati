#!/usr/bin/env python

"""
This file is a standalone python script that is tested with python 2.6.5
If Duplicati is producing a backup with the option --create-verification-file,
it will produce a .verification file on the backend, which can be verified
by this script. Simply run this script with the path to the backup
folder as input, and it will verify all .verification files in the folder.
"""

import sys
import os
import string
from xml.dom import minidom
from hashlib import sha256

""" Utility function to return byte chunks from a binary file """
def bytes_from_file(filename, chunksize=8192):
	with open(filename, "rb") as f:
		while True:
			chunk = f.read(chunksize)
			if chunk:
				for b in chunk:
					yield b
			else:
				break

""" Verifies a single .verification file """				
def verifyHashes(filename):
	errorCount = 0
	checked = 0

	if (not os.path.exists(filename)):
		print "Specified file does not exist: ", filename
		return -1
	
	folder = os.path.dirname(filename)
	doc = minidom.parse(filename)
	
	for file in doc.getElementsByTagName("File"):
		filename = file.attributes["name"].value
		hash = string.lower(file.firstChild.data)
		
		fullpath = os.path.join(folder, filename)
		if not os.path.exists(fullpath):
			print "File missing: ", fullpath
			errorCount += 1
		else:
			checked += 1
			print "Verifying file ", filename
			hashalg = sha256()
			for b in bytes_from_file(fullpath):
				hashalg.update(b)
			hashval = string.lower(hashalg.hexdigest())
			if hashval != hash:
				print "*** Hash check failed for file: ", fullpath
				errorCount += 1
				
	
	if errorCount > 0:
		print "Errors were found"
	else:
		print "No errors found"	
		
	return errorCount
	

if __name__ == "__main__":
	if len(sys.argv) != 1 and len(sys.argv) != 2:
		print "Usage:", len(sys.argv)
		print "DuplicatiVerify.py <path to verification file>"
		print "DuplicatiVerify.py <folder with verification files>"
		print "DuplicatiVerify.py (no arguments, uses current dir)"
	else:
		if len(sys.argv) == 1:
			argument = os.getcwd()
		else:
			argument = sys.argv[1]
		
		if not os.path.exists(argument):
			print "No such file or directory: ", argument
		else:
			if os.path.isfile(argument):
				verifyHashes(argument)
			else:
				files = 0
				for f in os.listdir(argument):
					if (f.endswith(".verification")):
						print "Verifying file: ", f
						files += 1
						verifyHashes(os.path.join(argument, f))
						
				if files == 0:
					print "No verification files in folder: ", argument