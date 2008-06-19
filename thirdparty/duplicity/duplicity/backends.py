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

"""Provides functions and classes for getting/sending files to destination"""

import os, socket, types, tempfile, time, sys
import log, path, dup_temp, file_naming, atexit
import base64, getpass, xml.dom.minidom, httplib, urllib
import socket, globals, re, string
from duplicity import tempdir

import urlparse_2_5 as urlparser

socket.setdefaulttimeout(globals.timeout)

class BackendException(Exception): pass
class ParsingException(Exception): pass


def straight_url(parsed_url):
	"""Return a URL from a urlparse object without a username or password."""

	# Get a copy of the network location without the username or password.
	straight_netloc = parsed_url.netloc.split('@')[-1]

	# Replace the full network location with the stripped copy.
	return parsed_url.geturl().replace(parsed_url.netloc, straight_netloc, 1)


def ParsedUrl(url_string):
	# These URL schemes have a backend with a notion of an RFC "network location".
	# The 'file' and 's3+http' schemes should not be in this list.
	# 'http' and 'https' are not actually used for duplicity backend urls, but are needed
	# in order to properly support urls returned from some webdav servers. adding them here
	# is a hack. we should instead not stomp on the url parsing module to begin with.
	urlparser.uses_netloc = [ 'ftp', 'hsi', 'rsync', 's3', 'scp', 'ssh', 'webdav', 'webdavs', 'http', 'https' ]

	# Do not transform or otherwise parse the URL path component.
	urlparser.uses_query = []
	urlparser.uses_fragment = []

	pu = urlparser.urlparse(url_string)

	# This happens for implicit local paths.
	if not pu.scheme:
		return pu

	# Our backends do not handle implicit hosts.
	if pu.scheme in urlparser.uses_netloc and not pu.hostname:
		log.FatalError('Bad %s:// URL syntax: %s' % (pu.scheme, url_string))

	# Our backends do not handle implicit relative paths.
	if not pu.scheme in urlparser.uses_netloc and not pu.path.startswith('//'):
		log.FatalError('Bad %s:// URL syntax: %s' % (pu.scheme, url_string))

	return pu


def get_backend(url_string):
	"""Return Backend object from url string, or None if not a url string"""
	"""If a protocol is unsupported a fatal error will be raised."""

	pu = ParsedUrl(url_string)

	# This happens for implicit local paths.
	if not pu.scheme:
		return None

	global protocol_class_dict
	try:
		backend_class = protocol_class_dict[pu.scheme]
	except KeyError:
		log.FatalError("Unknown scheme '%s'" % (pu.scheme,))
	return backend_class(pu)


class Backend:
	"""Represent a connection to the destination device/computer

	Classes that subclass this should implement the put, get, list,
	and delete methods.

	"""
	def __init__(self, parsed_url):
		self.parsed_url = parsed_url

	def put(self, source_path, remote_filename = None):
		"""Transfer source_path (Path object) to remote_filename (string)

		If remote_filename is None, get the filename from the last
		path component of pathname.

		"""
		if not remote_filename: remote_filename = source_path.get_filename()
		pass

	def get(self, remote_filename, local_path):
		"""Retrieve remote_filename and place in local_path"""
		local_path.setdata()
		pass

	def list(self):
		"""Return list of filenames (strings) present in backend"""
		pass

	def delete(self, filename_list):
		"""Delete each filename in filename_list, in order if possible"""
		pass

	def get_password(self):
		"""Get a password from the target url or from the environment."""

		if self.parsed_url.password:
			return self.parsed_url.password

		try:
			password = os.environ['FTP_PASSWORD']
		except KeyError:
			password = getpass.getpass("Password for '%s': " % self.parsed_url.hostname)
			os.environ['FTP_PASSWORD'] = password
		return password

	def munge_password(self, commandline):
		"""Remove password from commandline"""
		if self.parsed_url.password:
			return re.sub(self.parsed_url.password, '<passwd>', commandline)
		else:
			return commandline

	def run_command(self, commandline):
		"""Run given commandline with logging and error detection"""
		private = self.munge_password(commandline)
		log.Log("Running '%s'" % private, 5)
		if os.system(commandline):
			raise BackendException("Error running '%s'" % private)

	def run_command_persist(self, commandline):
		"""Run given commandline with logging and error detection
		repeating it several times if it fails"""
		private = self.munge_password(commandline)
		for n in range(1, globals.num_retries+1):
			log.Log("Running '%s' (attempt #%d)" % (private, n), 5)
			if not os.system(commandline):
				return
			log.Log("Running '%s' failed (attempt #%d)" % (private, n), 1)
			time.sleep(30)
		log.Log("Giving up trying to execute '%s' after %d attempts" % (private, globals.num_retries), 1)
		raise BackendException("Error running '%s'" % private)

	def popen(self, commandline):
		"""Run command and return stdout results"""
		private = self.munge_password(commandline)
		log.Log("Reading results of '%s'" % private, 5)
		fout = os.popen(commandline)
		results = fout.read()
		if fout.close():
			raise BackendException("Error running '%s'" % private)
		return results

	def popen_persist(self, commandline):
		"""Run command and return stdout results, repeating on failure"""
		private = self.munge_password(commandline)
		for n in range(1, globals.num_retries+1):
			log.Log("Reading results of '%s'" % private, 5)
			fout = os.popen(commandline)
			results = fout.read()
			result_status = fout.close()
			if not result_status:
				return results
			elif result_status == 1280 and self.parsed_url.scheme == 'ftp':
				# This squelches the "file not found" result fromm ncftpls when
				# the ftp backend looks for a collection that does not exist.
				return ''
			log.Log("Running '%s' failed (attempt #%d)" % (private, n), 1)
			time.sleep(30)
		log.Log("Giving up trying to execute '%s' after %d attempts" % (private, globals.num_retries), 1)
		raise BackendException("Error running '%s'" % private)

	def get_fileobj_read(self, filename, parseresults = None):
		"""Return fileobject opened for reading of filename on backend

		The file will be downloaded first into a temp file.  When the
		returned fileobj is closed, the temp file will be deleted.

		"""
		if not parseresults:
			parseresults = file_naming.parse(filename)
			assert parseresults, "Filename not correctly parsed"
		tdp = dup_temp.new_tempduppath(parseresults)
		self.get(filename, tdp)
		tdp.setdata()
		return tdp.filtered_open_with_delete("rb")

	def get_fileobj_write(self, filename, parseresults = None,
						  sizelist = None):
		"""Return fileobj opened for writing, write to backend on close

		The file will be encoded as specified in parseresults (or as
		read from the filename), and stored in a temp file until it
		can be copied over and deleted.

		If sizelist is not None, it should be set to an empty list.
		The number of bytes will be inserted into the list.

		"""
		if not parseresults:
			parseresults = file_naming.parse(filename)
			assert parseresults, "Filename %s not correctly parsed" % filename
		tdp = dup_temp.new_tempduppath(parseresults)

		def close_file_hook():
			"""This is called when returned fileobj is closed"""
			self.put(tdp, filename)
			if sizelist is not None:
				tdp.setdata()
				sizelist.append(tdp.getsize())
			tdp.delete()

		fh = dup_temp.FileobjHooked(tdp.filtered_open("wb"))
		fh.addhook(close_file_hook)
		return fh

	def get_data(self, filename, parseresults = None):
		"""Retrieve a file from backend, process it, return contents"""
		fin = self.get_fileobj_read(filename, parseresults)
		buf = fin.read()
		assert not fin.close()
		return buf

	def put_data(self, buffer, filename, parseresults = None):
		"""Put buffer into filename on backend after processing"""
		fout = self.get_fileobj_write(filename, parseresults)
		fout.write(buffer)
		assert not fout.close()

	def close(self):
		"""This is called when a connection is no longer needed"""
		pass


class LocalBackend(Backend):
	"""Use this backend when saving to local disk

	Urls look like file://testfiles/output.  Relative to root can be
	gotten with extra slash (file:///usr/local).

	"""
	def __init__(self, parsed_url):
		Backend.__init__(self, parsed_url)
		# The URL form "file:MyFile" is not a valid duplicity target.
		if not parsed_url.path.startswith( '//' ):
			raise BackendException( "Bad file:// path syntax." )
		self.remote_pathdir = path.Path(parsed_url.path[2:])

	def put(self, source_path, remote_filename = None, rename = None):
		"""If rename is set, try that first, copying if doesn't work"""
		if not remote_filename: remote_filename = source_path.get_filename()
		target_path = self.remote_pathdir.append(remote_filename)
		log.Log("Writing %s" % target_path.name, 6)
		if rename:
			try: source_path.rename(target_path)
			except OSError: pass
			else: return
		target_path.writefileobj(source_path.open("rb"))

	def get(self, filename, local_path):
		"""Get file and put in local_path (Path object)"""
		source_path = self.remote_pathdir.append(filename)
		local_path.writefileobj(source_path.open("rb"))

	def list(self):
		"""List files in that directory"""
		try:
			os.makedirs(self.remote_pathdir.base)
		except:
			pass
		return self.remote_pathdir.listdir()

	def delete(self, filename_list):
		"""Delete all files in filename list"""
		assert type(filename_list) is not types.StringType
		try:
			for filename in filename_list:
				self.remote_pathdir.append(filename).delete()
		except OSError, e: raise BackendException(str(e))


# The following can be redefined to use different shell commands from
# ssh or scp or to add more arguments.	However, the replacements must
# have the same syntax.  Also these strings will be executed by the
# shell, so shouldn't have strange characters in them.
scp_command = "scp"
sftp_command = "sftp"

# default to batch mode using public-key encryption
ssh_askpass = False

# user added ssh options
ssh_options = ""

class sshBackend(Backend):
	"""This backend copies files using scp.  List not supported"""
	def __init__(self, parsed_url):
		"""scpBackend initializer"""
		Backend.__init__(self, parsed_url)
		try:
			import pexpect
			self.pexpect = pexpect
		except ImportError:
			self.pexpect = None
		if not (self.pexpect and
				hasattr(self.pexpect, '__version__') and
				self.pexpect.__version__ >= '2.1'):
			log.FatalError("This backend requires the pexpect module version 2.1 or later."
						   "You can get pexpect from http://pexpect.sourceforge.net or "
						   "python-pexpect from your distro's repository.")
		
		# host string of form user@hostname:port
		self.host_string = parsed_url.netloc
		# make sure remote_dir is always valid
		if parsed_url.path:
			# remove leading '/'
			self.remote_dir = re.sub(r'^/', r'', parsed_url.path, 1)
		else:
			self.remote_dir = '.'
		self.remote_prefix = self.remote_dir + '/'
		# maybe use different ssh port
		if parsed_url.port:
			self.ssh_options = ssh_options + " -oPort=%s" % parsed_url.port
		else:
			self.ssh_options = ssh_options
		# set up password
		if ssh_askpass:
			self.password = self.get_password()
		else:
			self.password = ''

	def run_scp_command(self, commandline):
		""" Run an scp command, responding to password prompts """
		for n in range(1, globals.num_retries+1):
			log.Log("Running '%s' (attempt #%d)" % (commandline, n), 5)
			child = self.pexpect.spawn(commandline, timeout = globals.timeout)
			cmdloc = 0
			if ssh_askpass:
				state = "authorizing"
			else:
				state = "copying"
			while 1:
				if state == "authorizing":
					match = child.expect([self.pexpect.EOF,
										  self.pexpect.TIMEOUT,
										  "(?i)password:",
										  "(?i)permission denied",
										  "authenticity"],
										 timeout = globals.timeout)
					log.Log("State = %s, Before = '%s'" % (state, child.before.strip()), 9)
					if match == 0:
						log.Log("Failed to authenticate", 5)
						break
					elif match == 1:
						log.Log("Timeout waiting to authenticate", 5)
						break
					elif match == 2:
						child.sendline(self.password)
						state = "copying"
					elif match == 3:
						log.Log("Invalid SSH password", 1)
						break
					elif match == 4:
						log.Log("Remote host authentication failed (missing known_hosts entry?)", 1)
						break
				elif state == "copying":
					match = child.expect([self.pexpect.EOF,
										  self.pexpect.TIMEOUT,
										  "stalled",
										  "authenticity",
										  "ETA"],
										 timeout = globals.timeout)
					log.Log("State = %s, Before = '%s'" % (state, child.before.strip()), 9)
					if match == 0:
						break
					elif match == 1:
						log.Log("Timeout waiting for response", 5)
						break
					elif match == 2:
						state = "stalled"
					elif match == 3:
						log.Log("Remote host authentication failed (missing known_hosts entry?)", 1)
						break
				elif state == "stalled":
					match = child.expect([self.pexpect.EOF,
										  self.pexpect.TIMEOUT,
										  "ETA"],
										 timeout = globals.timeout)
					log.Log("State = %s, Before = '%s'" % (state, child.before.strip()), 9)
					if match == 0:
						break
					elif match == 1:
						log.Log("Stalled for too long, aborted copy", 5)
						break
					elif match == 2:
						state = "copying"
			child.close(force = True)
			if child.exitstatus == 0:
				return
			log.Log("Running '%s' failed (attempt #%d)" % (commandline, n), 1)
			time.sleep(30)
		log.Log("Giving up trying to execute '%s' after %d attempts" % (commandline, globals.num_retries), 1)
		raise BackendException("Error running '%s'" % commandline)

	def run_sftp_command(self, commandline, commands):
		""" Run an sftp command, responding to password prompts, passing commands from list """
		for n in range(1, globals.num_retries+1):
			log.Log("Running '%s' (attempt #%d)" % (commandline, n), 5)
			child = self.pexpect.spawn(commandline, timeout = globals.timeout)
			cmdloc = 0
			while 1:
				match = child.expect([self.pexpect.EOF,
									  self.pexpect.TIMEOUT,
									  "sftp>",
									  "(?i)password:",
									  "(?i)permission denied",
									  "authenticity",
									  "(?i)no such file or directory"])
				log.Log("State = sftp, Before = '%s'" % (child.before.strip()), 9)
				if match == 0:
					break
				elif match == 1:
					log.Log("Timeout waiting for response", 5)
					break
				if match == 2:
					if cmdloc < len(commands):
						command = commands[cmdloc]
						log.Log("sftp command: '%s'" % (command,), 5)
						child.sendline(command)
						cmdloc += 1
					else:
						command = 'quit'
						child.sendline(command)
						res = child.before
				elif match == 3:
					child.sendline(self.password)
				elif match == 4:
					log.Log("Invalid SSH password", 1)
					break
				elif match == 5:
					log.Log("Host key authenticity could not be verified (missing known_hosts entry?)", 1)
					break
				elif match == 6:
					log.Log("Remote file or directory '%s' does not exist" % self.remote_dir, 1)
					break
			child.close(force = True)
			if child.exitstatus == 0:
				return res
			log.Log("Running '%s' failed (attempt #%d)" % (commandline, n), 1)
			time.sleep(30)
		log.Log("Giving up trying to execute '%s' after %d attempts" % (commandline, globals.num_retries), 1)
		raise BackendException("Error running '%s'" % commandline)

	def put(self, source_path, remote_filename = None):
		"""Use scp to copy source_dir/filename to remote computer"""
		if not remote_filename: remote_filename = source_path.get_filename()
		commandline = "%s %s %s %s:%s%s" % \
					  (scp_command, self.ssh_options, source_path.name, self.host_string,
					   self.remote_prefix, remote_filename)
		self.run_scp_command(commandline)

	def get(self, remote_filename, local_path):
		"""Use scp to get a remote file"""
		commandline = "%s %s %s:%s%s %s" % \
					  (scp_command, self.ssh_options, self.host_string, self.remote_prefix,
					   remote_filename, local_path.name)
		self.run_scp_command(commandline)
		local_path.setdata()
		if not local_path.exists():
			raise BackendException("File %s not found" % local_path.name)

	def list(self):
		"""List files available for scp

		Note that this command can get confused when dealing with
		files with newlines in them, as the embedded newlines cannot
		be distinguished from the file boundaries.
		"""
		commands = ["mkdir %s" % (self.remote_dir,),
					"cd %s" % (self.remote_dir,),
					"ls -1"]
		commandline = ("%s %s %s" % (sftp_command, self.ssh_options, self.host_string))
		l = self.run_sftp_command(commandline, commands).split('\n')[1:]
		return filter(lambda x: x, map(string.strip, l))

	def delete(self, filename_list):
		"""Runs sftp rm to delete files.  Files must not require quoting"""
		commands = ["cd %s" % (self.remote_dir,)]
		for fn in filename_list:
			commands.append("rm %s" % fn)
		commandline = ("%s %s %s" % (sftp_command, self.ssh_options, self.host_string))
		self.run_sftp_command(commandline, commands)


class ftpBackend(Backend):
	"""Connect to remote store using File Transfer Protocol"""
	def __init__(self, parsed_url):
		Backend.__init__(self, parsed_url)

		# we expect an error return, so go low-level and ignore it
		try:
			p = os.popen("ncftpls -v")
			fout = p.read()
			ret = p.close()
		except:
			pass
		# the expected error is 8 in the high-byte and some output
		if ret != 0x0800 or not fout:
			log.FatalError("NcFTP not found:  Please install NcFTP version 3.1.9 or later")

		# version is the second word of the first line
		version = fout.split('\n')[0].split()[1]
		if version < "3.1.9":
			log.FatalError("NcFTP too old:  Duplicity requires NcFTP version 3.1.9 or later")
		log.Log("NcFTP version is %s" % version, 4)

		self.parsed_url = parsed_url

		self.url_string = straight_url(self.parsed_url)

		# Use an explicit directory name.
		if self.url_string[-1] != '/':
			self.url_string += '/'

		self.password = self.get_password()

		if globals.ftp_connection == 'regular':
			self.conn_opt = '-E'
		else:
			self.conn_opt = '-F'

 		self.tempfile, self.tempname = tempdir.default().mkstemp()
		os.write(self.tempfile, "host %s\n" % self.parsed_url.hostname)
 		os.write(self.tempfile, "user %s\n" % self.parsed_url.username)
 		os.write(self.tempfile, "pass %s\n" % self.password)
 		os.close(self.tempfile)
		self.flags = "-f %s %s -t %s" % \
			(self.tempname, self.conn_opt, globals.timeout)
		if parsed_url.port != None and parsed_url.port != 21:
			self.flags += " -P '%s'" % (parsed_url.port)

	def put(self, source_path, remote_filename = None):
		"""Transfer source_path to remote_filename"""
		remote_path = os.path.join(urllib.unquote(self.parsed_url.path.lstrip('/')), remote_filename).rstrip()
		commandline = "ncftpput %s -m -V -C '%s' '%s'" % \
					  (self.flags, source_path.name, remote_path)
		self.run_command_persist(commandline)

	def get(self, remote_filename, local_path):
		"""Get remote filename, saving it to local_path"""
		remote_path = os.path.join(urllib.unquote(self.parsed_url.path), remote_filename).rstrip()
		commandline = "ncftpget %s -V -C '%s' '%s' '%s'" % \
					  (self.flags, self.parsed_url.hostname, remote_path.lstrip('/'), local_path.name)
		self.run_command_persist(commandline)
		local_path.setdata()

	def list(self):
		"""List files in directory"""
		# try for a long listing to avoid connection reset
		commandline = "ncftpls %s -l '%s'" % \
					  (self.flags, self.url_string)
		l = self.popen_persist(commandline).split('\n')
		l = filter(lambda x: x, l)
		if not l:
			return l
		# if long list is not empty, get short list of names only
		commandline = "ncftpls -x '' %s '%s'" % \
					  (self.flags, self.url_string)
		l = self.popen_persist(commandline).split('\n')
		l = [x.split()[-1] for x in l if x]
		return l

	def delete(self, filename_list):
		"""Delete files in filename_list"""
		for filename in filename_list:
			commandline = "ncftpls -x '' %s -X 'DELE %s' '%s'" % \
						  (self.flags, filename, self.url_string)
			self.popen_persist(commandline)


class rsyncBackend(Backend):
	"""Connect to remote store using rsync

	rsync backend contributed by Sebastian Wilhelmi <seppi@seppi.de>

	"""
	def __init__(self, parsed_url):
		"""rsyncBackend initializer"""
		Backend.__init__(self, parsed_url)
		user, host = parsed_url.netloc.split('@')
		if parsed_url.password:
			user = user.split(':')[0]
		mynetloc = '%s@%s' % (user, host)
		# module url: rsync://user@host::/modname/path
		# rsync via ssh/rsh: rsync://user@host//some_absolute_path
		#      -or-          rsync://user@host/some_relative_path
		if parsed_url.netloc.endswith("::"):
			# its a module path
			self.url_string = "%s%s" % (mynetloc, parsed_url.path.lstrip('/'))
		elif parsed_url.path.startswith("//"):
			# its an absolute path
			self.url_string = "%s:/%s" % (mynetloc.rstrip(':'), parsed_url.path.lstrip('/'))
		else:
			# its a relative path
			self.url_string = "%s:%s" % (mynetloc.rstrip(':'), parsed_url.path.lstrip('/'))
		if self.url_string[-1] != '/':
			self.url_string += '/'

	def put(self, source_path, remote_filename = None):
		"""Use rsync to copy source_dir/filename to remote computer"""
		if not remote_filename: remote_filename = source_path.get_filename()
		remote_path = os.path.join(self.url_string, remote_filename)
		commandline = "rsync %s %s" % (source_path.name, remote_path)
		self.run_command(commandline)

	def get(self, remote_filename, local_path):
		"""Use rsync to get a remote file"""
		remote_path = os.path.join (self.url_string, remote_filename)
		commandline = "rsync %s %s" % (remote_path, local_path.name)
		self.run_command(commandline)
		local_path.setdata()
		if not local_path.exists():
			raise BackendException("File %s not found" % local_path.name)

	def list(self):
		"""List files"""
		def split (str):
			line = str.split ()
			if len (line) > 4 and line[4] != '.':
				return line[4]
			else:
				return None
		commandline = "rsync %s" % self.url_string
		return filter(lambda x: x, map (split, self.popen(commandline).split('\n')))

	def delete(self, filename_list):
		"""Delete files."""
		delete_list = filename_list
		dont_delete_list = []
		for file in self.list ():
			if file in delete_list:
				delete_list.remove (file)
			else:
				dont_delete_list.append (file)
		if len (delete_list) > 0:
			raise BackendException("Files %s not found" % str (delete_list))

		dir = tempfile.mkdtemp()
		exclude, exclude_name = tempdir.default().mkstemp_file()
		to_delete = [exclude_name]
		for file in dont_delete_list:
			path = os.path.join (dir, file)
			to_delete.append (path)
			f = open (path, 'w')
			print >>exclude, file
			f.close()
		exclude.close()
		commandline = ("rsync --recursive --delete --exclude-from=%s %s/ %s" %
					   (exclude_name, dir, self.url_string))
		self.run_command(commandline)
		for file in to_delete:
			os.unlink (file)
		os.rmdir (dir)


class BotoBackend(Backend):
	"""
	Backend for Amazon's Simple Storage System, (aka Amazon S3), though
	the use of the boto module, (http://code.google.com/p/boto/).

	To make use of this backend you must export the environment variables
	AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY with your Amazon Web 
	Services key id and secret respectively.
	"""

	def __init__(self, parsed_url):
		Backend.__init__(self, parsed_url)
		try:
			from boto.s3.connection import S3Connection
			from boto.s3.key import Key
			assert hasattr(S3Connection, 'lookup')

			# Newer versions of boto default to using virtual hosting for
			# buckets. This is bad because it will break backups stored in
			# buckets that contain upper-case characters in the name.
			try:
				from boto.s3.connection import OrdinaryCallingFormat
				calling_format = OrdinaryCallingFormat()
			except ImportError:
				calling_format = None
		except ImportError:
			log.FatalError("This backend requires boto library, version 0.9d or later, "
						   "(http://code.google.com/p/boto/).")

		if not os.environ.has_key('AWS_ACCESS_KEY_ID'):
			raise BackendException("The AWS_ACCESS_KEY_ID environment variable is not set.")

		if not os.environ.has_key('AWS_SECRET_ACCESS_KEY'):
			raise BackendException("The AWS_SECRET_ACCESS_KEY environment variable is not set.")

 		if parsed_url.scheme == 's3+http':
			# Use the default Amazon S3 host.
 			self.conn = S3Connection()
 		else:
			assert parsed_url.scheme == 's3'
			self.conn = S3Connection(host=parsed_url.hostname)

		if hasattr(self.conn, 'calling_format'):
			self.conn.calling_format = calling_format

		# This folds the null prefix and all null parts, which means that:
		#  //MyBucket/ and //MyBucket are equivalent.
		#  //MyBucket//My///My/Prefix/ and //MyBucket/My/Prefix are equivalent.
		self.url_parts = filter(lambda x: x != '', parsed_url.path.split('/'))

		if self.url_parts:
				self.bucket_name = self.url_parts.pop(0)
		else:
				# Duplicity hangs if boto gets a null bucket name.
				# HC: Caught a socket error, trying to recover
				raise BackendException('Boto requires a bucket name.')

		self.bucket = self.conn.lookup(self.bucket_name)
		self.key_class = Key

		if self.url_parts:
				self.key_prefix = '%s/' % '/'.join(self.url_parts)
		else:
				self.key_prefix = ''

		self.straight_url = straight_url(parsed_url)


	def put(self, source_path, remote_filename=None):
		if not self.bucket:
			self.bucket = self.conn.create_bucket(self.bucket_name)
		if not remote_filename:
			remote_filename = source_path.get_filename()
		key = self.key_class(self.bucket)
		key.key = self.key_prefix + remote_filename
		for n in range(1, globals.num_retries+1):
			log.Log("Uploading %s/%s" % (self.straight_url, remote_filename), 5)
			try:
				key.set_contents_from_filename(source_path.name, {'Content-Type': 'application/octet-stream'})
				return
			except:
				pass
			log.Log("Upload '%s/%s' failed (attempt #%d)" % (self.straight_url, remote_filename, n), 1)
			time.sleep(30)
		log.Log("Giving up trying to upload %s/%s after %d attempts" % (self.straight_url, remote_filename, globals.num_retries), 1)
		raise BackendException("Error uploading %s/%s" % (self.straight_url, remote_filename))
	
	def get(self, remote_filename, local_path):
		key = self.key_class(self.bucket)
		key.key = self.key_prefix + remote_filename
		for n in range(1, globals.num_retries+1):
			log.Log("Downloading %s/%s" % (self.straight_url, remote_filename), 5)
			try:
				key.get_contents_to_filename(local_path.name)
				local_path.setdata()
				return
			except:
				pass
			log.Log("Download %s/%s failed (attempt #%d)" % (self.straight_url, remote_filename, n), 1)
			time.sleep(30)
		log.Log("Giving up trying to download %s/%s after %d attempts" % (self.straight_url, remote_filename, globals.num_retries), 1)
		raise BackendException("Error downloading %s/%s" % (self.straight_url, remote_filename))

	def list(self):
		filename_list = []
		if self.bucket:
			# We add a 'd' to the prefix to make sure it is not null (for boto) and
			# to optimize the listing of our filenames, which always begin with 'd'.
			# This will cause a failure in the regression tests as below:
			#   FAIL: Test basic backend operations
			#   <tracback snipped>
			#   AssertionError: Got list: []
			#   Wanted: ['testfile']
			# Because of the need for this optimization, it should be left as is.
			#for k in self.bucket.list(prefix = self.key_prefix + 'd', delimiter = '/'):
			for k in self.bucket.list(prefix = self.key_prefix, delimiter = '/'):
				try:
					filename = k.key.replace(self.key_prefix, '', 1)
					filename_list.append(filename)
					log.Log("Listed %s/%s" % (self.straight_url, filename), 9)
				except AttributeError:
					pass
		return filename_list

	def delete(self, filename_list):
		for filename in filename_list:
			self.bucket.delete_key(self.key_prefix + filename)
			log.Log("Deleted %s/%s" % (self.straight_url, filename), 9)


class webdavBackend(Backend):
	"""Backend for accessing a WebDAV repository.
	
	webdav backend contributed in 2006 by Jesper Zedlitz <jesper@zedlitz.de>
	"""
	listbody = """\
<?xml version="1.0" encoding="utf-8" ?>
<D:propfind xmlns:D="DAV:">
  <D:allprop/>
</D:propfind>

"""
	
	"""Connect to remote store using WebDAV Protocol"""
	def __init__(self, parsed_url):
		Backend.__init__(self, parsed_url)
		self.headers = {}
		self.parsed_url = parsed_url

		
		if parsed_url.path:
			foldpath = re.compile('/+')
			self.directory = foldpath.sub('/', parsed_url.path + '/' )
		else:
			self.directory = '/'
		
		log.Log("Using WebDAV host %s" % (parsed_url.hostname,), 5)
		log.Log("Using WebDAV directory %s" % (self.directory,), 5)
		log.Log("Using WebDAV protocol %s" % (globals.webdav_proto,), 5)
		
		password = self.get_password()

 		if parsed_url.scheme == 'webdav':
 			self.conn = httplib.HTTPConnection(parsed_url.hostname)
 		elif parsed_url.scheme == 'webdavs':
 			self.conn = httplib.HTTPSConnection(parsed_url.hostname)
 		else:
 			raise BackendException("Unknown URI scheme: %s" % (parsed_url.scheme))

		self.headers['Authorization'] = 'Basic ' + base64.encodestring(parsed_url.username+':'+ password).strip()
		
		# check password by connection to the server
		self.conn.request("OPTIONS", self.directory, None, self.headers)
		response = self.conn.getresponse()
		response.read()
		if response.status !=  200:
			raise BackendException((response.status, response.reason))

	def _getText(self,nodelist):
		rc = ""
		for node in nodelist:
			if node.nodeType == node.TEXT_NODE:
				rc = rc + node.data
		return rc

	def close(self):
		self.conn.close()
		
	def list(self):
		"""List files in directory"""
		for n in range(1, globals.num_retries+1):
			log.Log("Listing directory %s on WebDAV server" % (self.directory,), 5)
			self.headers['Depth'] = "1"
			self.conn.request("PROPFIND", self.directory, self.listbody, self.headers)
			del self.headers['Depth']
			response = self.conn.getresponse()
			if response.status == 207:
				document = response.read()
				break
			log.Log("WebDAV PROPFIND attempt #%d failed: %s %s" % (n, response.status, response.reason), 5)
			if n == globals.num_retries +1:
				log.Log("WebDAV backend giving up after %d attempts to PROPFIND %s" % (globals.num_retries, self.directory), 1)
				raise BackendException((response.status, response.reason))

		log.Log("%s" % (document,), 6)
		dom = xml.dom.minidom.parseString(document)
		result = []
		for href in dom.getElementsByTagName('D:href'):
			filename = self.__taste_href(href)
			if not filename is None:
				result.append(filename)
		return result

	def __taste_href(self, href):
		"""
		Internal helper to taste the given href node and, if
		it is a duplicity file, collect it as a result file.

		@returns A matching filename, or None if the href did
		         not match.
		"""
		raw_filename = self._getText(href.childNodes).strip()
		parsed_url = urlparser.urlparse(urllib.unquote(raw_filename))
		filename = parsed_url.path
		log.Debug("webdav path decoding and translation: "\
			  "%s -> %s" % (raw_filename, filename))

		# at least one WebDAV server returns files in the form
		# of full URL:s. this may or may not be
		# according to the standard, but regardless we
		# feel we want to bail out if the hostname
		# does not match until someone has looked into
		# what the WebDAV protocol mandages.
		if not parsed_url.hostname is None \
		   and not (parsed_url.hostname == self.parsed_url.hostname):
			m =  "Received filename was in the form of a "\
			    "full url, but the hostname (%s) did "\
			    "not match that of the webdav backend "\
			    "url (%s) - aborting as a conservative "\
			    "safety measure. If this happens to you, "\
			    "please report the problem"\
			    "" % (parsed_url.hostname,
				  self.parsed_url.hostname)
			raise BackendException(m)
					       
		if filename.startswith(self.directory):
			filename = filename.replace(self.directory,'',1)
			return filename
		else:
			return None

	def get(self, remote_filename, local_path):
		"""Get remote filename, saving it to local_path"""
		url = self.directory + remote_filename
		target_file = local_path.open("wb")
		for n in range(1, globals.num_retries+1):
			log.Log("Retrieving %s from WebDAV server" % (url ,), 5)
			self.conn.request("GET", url, None, self.headers)
			response = self.conn.getresponse()		
			if response.status == 200:
				target_file.write(response.read())
				assert not target_file.close()
				local_path.setdata()
				return
			log.Log("WebDAV GET attempt #%d failed: %s %s" % (n, response.status, response.reason), 5)
		log.Log("WebDAV backend giving up after %d attempts to GET %s" % (globals.num_retries, url), 1)
		raise BackendException((response.status, response.reason))

	def put(self, source_path, remote_filename = None):
		"""Transfer source_path to remote_filename"""
		if not remote_filename: 
			remote_filename = source_path.get_filename()
		url = self.directory + remote_filename
		source_file = source_path.open("rb")
		for n in range(1, globals.num_retries+1):
			log.Log("Saving %s on WebDAV server" % (url ,), 5)
			self.conn.request("PUT", url, source_file.read(), self.headers)
			response = self.conn.getresponse()
			if response.status == 201:
				response.read()
				assert not source_file.close()
				return
			log.Log("WebDAV PUT attempt #%d failed: %s %s" % (n, response.status, response.reason), 5)
		log.Log("WebDAV backend giving up after %d attempts to PUT %s" % (globals.num_retries, url), 1)
		raise BackendException((response.status, response.reason))

	def delete(self, filename_list):
		"""Delete files in filename_list"""
		for filename in filename_list:
			url = self.directory + filename
			for n in range(1, globals.num_retries+1):
				log.Log("Deleting %s from WebDAV server" % (url ,), 5)
				self.conn.request("DELETE", url, None, self.headers)
				response = self.conn.getresponse()
				if response.status == 204:
					response.read()
					break
				log.Log("WebDAV DELETE attempt #%d failed: %s %s" % (n, response.status, response.reason), 5)
				if n == globals.num_retries +1:
					log.Log("WebDAV backend giving up after %d attempts to DELETE %s" % (globals.num_retries, url), 1)
					raise BackendException((response.status, response.reason))

hsi_command = "hsi"
class hsiBackend(Backend):
	def __init__(self, parsed_url):
		Backend.__init__(self, parsed_url)
		self.host_string = parsed_url.hostname
		self.remote_dir = parsed_url.path
		if self.remote_dir: self.remote_prefix = self.remote_dir + "/"
		else: self.remote_prefix = ""

	def put(self, source_path, remote_filename = None):
		if not remote_filename: remote_filename = source_path.get_filename()
		commandline = '%s "put %s : %s%s"' % (hsi_command,source_path.name,self.remote_prefix,remote_filename)
		try:
			self.run_command(commandline)
		except:
			print commandline

	def get(self, remote_filename, local_path):
		commandline = '%s "get %s : %s%s"' % (hsi_command, local_path.name, self.remote_prefix, remote_filename)
		self.run_command(commandline)
		local_path.setdata()
		if not local_path.exists():
			raise BackendException("File %s not found" % local_path.name)

	def list(self):
		commandline = '%s "ls -l %s"' % (hsi_command, self.remote_dir)
		l = os.popen3(commandline)[2].readlines()[3:]
		for i in range(0,len(l)):
			l[i] = l[i].split()[-1]
		print filter(lambda x: x, l)
		return filter(lambda x: x, l)

	def delete(self, filename_list):
		assert len(filename_ist) > 0
		pathlist = map(lambda fn: self.remote_prefix + fn, filename_list)
		for fn in filename_list:
			commandline = '%s "rm %s%s"' % (hsi_command, self.remote_prefix, fn)
			self.run_command(commandline)

# Dictionary relating protocol strings to backend_object classes.
protocol_class_dict = {"file": LocalBackend,
					   "ftp": ftpBackend,
					   "hsi": hsiBackend,
					   "rsync": rsyncBackend,
					   "scp": sshBackend,
					   "ssh": sshBackend,
					   "s3": BotoBackend,
					   "s3+http": BotoBackend,
					   "webdav": webdavBackend,
					   "webdavs": webdavBackend,
					   }
