# This module is very old and useless in this day and age!  It will be
# removed in a few years (ie, 2009 or so...)

import warnings
warnings.warn("The regcheck module has been pending deprecation since build 210",
	      category=warnings.PendingDeprecationWarning)

import win32con
import regutil
import win32api
import os
import sys


def CheckRegisteredExe(exename):
	try:
		os.stat(win32api.RegQueryValue(regutil.GetRootKey()  , regutil.GetAppPathsKey() + "\\" + exename))
#	except SystemError:
	except (os.error,win32api.error):
		print "Registration of %s - Not registered correctly" % exename

def CheckPathString(pathString):
	import string
	for path in string.split(pathString, ";"):
		if not os.path.isdir(path):
			return "'%s' is not a valid directory!" % path
	return None

def CheckPythonPaths(verbose):
	if verbose: print "Python Paths:"
	# Check the core path
	if verbose: print "\tCore Path:",
	try:
		appPath = win32api.RegQueryValue(regutil.GetRootKey(), regutil.BuildDefaultPythonKey() + "\\PythonPath")
	except win32api.error, (code, fn, desc):
		print "** does not exist - ", desc
	problem = CheckPathString(appPath)
	if problem:
		print problem
	else:
		if verbose: print appPath
	
	key = win32api.RegOpenKey(regutil.GetRootKey(), regutil.BuildDefaultPythonKey() + "\\PythonPath", 0, win32con.KEY_READ)
	try:
		keyNo = 0
		while 1:
			try:
				appName = win32api.RegEnumKey(key, keyNo)
				appPath = win32api.RegQueryValue(key, appName)
				if verbose: print "\t"+appName+":",
				if appPath:
					problem = CheckPathString(appPath)
					if problem:
						print problem
					else:
						if verbose: print appPath
				else:
					if verbose: print "(empty)"
				keyNo = keyNo + 1
			except win32api.error:
				break
	finally:
		win32api.RegCloseKey(key)

def CheckHelpFiles(verbose):
	if verbose: print "Help Files:"
	try:
		key = win32api.RegOpenKey(regutil.GetRootKey(), regutil.BuildDefaultPythonKey() + "\\Help", 0, win32con.KEY_READ)
	except win32api.error, (code, fn, details):
		import winerror
		if code!=winerror.ERROR_FILE_NOT_FOUND:
			raise win32api.error, (code, fn, details)
		return
		
	try:
		keyNo = 0
		while 1:
			try:
				helpDesc = win32api.RegEnumKey(key, keyNo)
				helpFile = win32api.RegQueryValue(key, helpDesc)
				if verbose: print "\t"+helpDesc+":",
				# query the os section.
				try:
					os.stat(helpFile )
					if verbose: print helpFile
				except os.error:
					print "** Help file %s does not exist" % helpFile
				keyNo = keyNo + 1
			except win32api.error, (code, fn, desc):
				import winerror
				if code!=winerror.ERROR_NO_MORE_ITEMS:
					raise win32api.error, (code, fn, desc)
				break
	finally:
		win32api.RegCloseKey(key)

def CheckRegisteredModules(verbose):
	# Check out all registered modules.
	k=regutil.BuildDefaultPythonKey() + "\\Modules"
	try:
		keyhandle = win32api.RegOpenKey(regutil.GetRootKey(), k)
		print "WARNING: 'Modules' registry entry is deprectated and evil!"
	except win32api.error, (code, fn, details):
		import winerror
		if code!=winerror.ERROR_FILE_NOT_FOUND:
			raise win32api.error, (code, fn, details)
		return

def CheckRegistry(verbose=0):
	# check the registered modules
	if verbose and os.environ.has_key('pythonpath'):
		print "Warning - PythonPath in environment - please check it!"
	# Check out all paths on sys.path
	
	CheckPythonPaths(verbose)
	CheckHelpFiles(verbose)
	CheckRegisteredModules(verbose)
	CheckRegisteredExe("Python.exe")

if __name__=='__main__':
	if len(sys.argv)>1 and sys.argv[1]=='-q':
		verbose = 0
	else:
		verbose = 1
	CheckRegistry(verbose)
