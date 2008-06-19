import win32wnet
import sys
from winnetwk import *
import os

possible_shares = []

def _doDumpHandle(handle, level = 0):
		indent = " " * level
		while 1:
			items = win32wnet.WNetEnumResource(handle, 0)
			if len(items)==0:
				break
			for item in items:
				try:
					if item.dwDisplayType == RESOURCEDISPLAYTYPE_SHARE:
						print indent + "Have share with name:", item.lpRemoteName
						possible_shares.append(item)
					elif item.dwDisplayType == RESOURCEDISPLAYTYPE_GENERIC:
						print indent + "Have generic resource with name:", item.lpRemoteName
					else:
						# Try generic!
						print indent + "Enumerating " + item.lpRemoteName,
						k = win32wnet.WNetOpenEnum(RESOURCE_GLOBALNET, RESOURCETYPE_ANY,0,item)
						print
						_doDumpHandle(k, level + 1)
						win32wnet.WNetCloseEnum(k) # could do k.Close(), but this is a good test!
				except win32wnet.error, details:
					print indent + "Couldn't enumerate this resource: " + details[2]

def TestOpenEnum():
	print "Enumerating all resources on the network - this may take some time..."
	handle = win32wnet.WNetOpenEnum(RESOURCE_GLOBALNET,RESOURCETYPE_ANY,0,None)

	try:
		_doDumpHandle(handle)
	finally:
		handle.Close()
	print "Finished dumping all resources."

def TestConnection():
	if len(possible_shares)==0:
		print "Couldn't find any potential shares to connect to"
		return
	localName = "Z:" # need better way!
	for share in possible_shares:
		print "Attempting connection of", localName, "to", share.lpRemoteName
		try:
			win32wnet.WNetAddConnection2(share.dwType, localName, share.lpRemoteName)
		except win32wnet.error, details:
			print "Couldn't connect: " + details[2]
			continue
		# Have a connection.
		try:
			fname = os.path.join(localName + "\\", os.listdir(localName + "\\")[0])
			try:
				print "Universal name of '%s' is '%s'" % (fname, win32wnet.WNetGetUniversalName(fname))
			except win32wnet.error, details:
				print "Couldn't get universal name of '%s': %s" % (fname, details[2])
			print "User name for this connection is", win32wnet.WNetGetUser(localName)
		finally:
			win32wnet.WNetCancelConnection2(localName, 0, 0)
			# Only do the first share that succeeds.
			break

def TestGetUser():
	u = win32wnet.WNetGetUser()
	print "Current global user is", `u`
	if u != win32wnet.WNetGetUser(None):
		raise RuntimeError, "Default value didnt seem to work!"

TestGetUser()
TestOpenEnum()
TestConnection()
