"""Interface to GNU Privacy Guard (GnuPG)

GnuPGInterface is a Python module to interface with GnuPG.
It concentrates on interacting with GnuPG via filehandles,
providing access to control GnuPG via versatile and extensible means.

This module is based on GnuPG::Interface, a Perl module by the same author.

Normally, using this module will involve creating a
GnuPG object, setting some options in it's 'options' data member
(which is of type Options), creating some pipes
to talk with GnuPG, and then calling the run() method, which will
connect those pipes to the GnuPG process. run() returns a
Process object, which contains the filehandles to talk to GnuPG with.

Example code:

>>> import GnuPGInterface
>>> 
>>> plaintext  = "Three blind mice"
>>> passphrase = "This is the passphrase"
>>> 
>>> gnupg = GnuPGInterface.GnuPG()
>>> gnupg.options.armor = 1
>>> gnupg.options.meta_interactive = 0
>>> gnupg.options.extra_args.append('--no-secmem-warning')
>>> 
>>> # Normally we might specify something in
>>> # gnupg.options.recipients, like
>>> # gnupg.options.recipients = [ '0xABCD1234', 'bob@foo.bar' ]
>>> # but since we're doing symmetric-only encryption, it's not needed.
>>> # If you are doing standard, public-key encryption, using
>>> # --encrypt, you will need to specify recipients before
>>> # calling gnupg.run()
>>> 
>>> # First we'll encrypt the test_text input symmetrically
>>> p1 = gnupg.run(['--symmetric'],
...                create_fhs=['stdin', 'stdout', 'passphrase'])
>>> 
>>> p1.handles['passphrase'].write(passphrase)
>>> p1.handles['passphrase'].close()
>>> 
>>> p1.handles['stdin'].write(plaintext)
>>> p1.handles['stdin'].close()
>>> 
>>> ciphertext = p1.handles['stdout'].read()
>>> p1.handles['stdout'].close()
>>> 
>>> # process cleanup
>>> p1.wait()
>>> 
>>> # Now we'll decrypt what we just encrypted it,
>>> # using the convience method to get the
>>> # passphrase to GnuPG
>>> gnupg.passphrase = passphrase
>>> 
>>> p2 = gnupg.run(['--decrypt'], create_fhs=['stdin', 'stdout'])
>>> 
>>> p2.handles['stdin'].write(ciphertext)
>>> p2.handles['stdin'].close()
>>> 
>>> decrypted_plaintext = p2.handles['stdout'].read()
>>> p2.handles['stdout'].close()
>>> 
>>> # process cleanup
>>> p2.wait()
>>> 
>>> # Our decrypted plaintext:
>>> decrypted_plaintext
'Three blind mice'
>>>
>>> # ...and see it's the same as what we orignally encrypted
>>> assert decrypted_plaintext == plaintext, \
          "GnuPG decrypted output does not match original input"
>>>
>>>
>>> ##################################################
>>> # Now let's trying using run()'s attach_fhs paramter
>>> 
>>> # we're assuming we're running on a unix...
>>> input = open('/etc/motd')
>>> 
>>> p1 = gnupg.run(['--symmetric'], create_fhs=['stdout'],
...                                 attach_fhs={'stdin': input})
>>>
>>> # GnuPG will read the stdin from /etc/motd
>>> ciphertext = p1.handles['stdout'].read()
>>>
>>> # process cleanup
>>> p1.wait()
>>> 
>>> # Now let's run the output through GnuPG
>>> # We'll write the output to a temporary file,
>>> import tempfile
>>> temp = tempfile.TemporaryFile()
>>> 
>>> p2 = gnupg.run(['--decrypt'], create_fhs=['stdin'],
...                               attach_fhs={'stdout': temp})
>>> 
>>> # give GnuPG our encrypted stuff from the first run
>>> p2.handles['stdin'].write(ciphertext)
>>> p2.handles['stdin'].close()
>>> 
>>> # process cleanup
>>> p2.wait()
>>> 
>>> # rewind the tempfile and see what GnuPG gave us
>>> temp.seek(0)
>>> decrypted_plaintext = temp.read()
>>> 
>>> # compare what GnuPG decrypted with our original input
>>> input.seek(0)
>>> input_data = input.read()
>>>
>>> assert decrypted_plaintext == input_data, \
           "GnuPG decrypted output does not match original input"

To do things like public-key encryption, simply pass do something
like:

gnupg.passphrase = 'My passphrase'
gnupg.options.recipients = [ 'bob@foobar.com' ]
gnupg.run( ['--sign', '--encrypt'], create_fhs=..., attach_fhs=...)

Here is an example of subclassing GnuPGInterface.GnuPG,
so that it has an encrypt_string() method that returns
ciphertext.

>>> import GnuPGInterface
>>> 
>>> class MyGnuPG(GnuPGInterface.GnuPG):
...
...     def __init__(self):
...         GnuPGInterface.GnuPG.__init__(self)
...         self.setup_my_options()
...
...     def setup_my_options(self):
...         self.options.armor = 1
...         self.options.meta_interactive = 0
...         self.options.extra_args.append('--no-secmem-warning')
...
...     def encrypt_string(self, string, recipients):
...        gnupg.options.recipients = recipients   # a list!
...        
...        proc = gnupg.run(['--encrypt'], create_fhs=['stdin', 'stdout'])
...        
...        proc.handles['stdin'].write(string)
...        proc.handles['stdin'].close()
...                
...        output = proc.handles['stdout'].read()
...        proc.handles['stdout'].close()
...
...        proc.wait()
...        return output
...
>>> gnupg = MyGnuPG()
>>> ciphertext = gnupg.encrypt_string("The secret", ['0x260C4FA3'])
>>>
>>> # just a small sanity test here for doctest
>>> import types
>>> assert isinstance(ciphertext, types.StringType), \
           "What GnuPG gave back is not a string!"

Here is an example of generating a key:
>>> import GnuPGInterface
>>> gnupg = GnuPGInterface.GnuPG()
>>> gnupg.options.meta_interactive = 0
>>>
>>> # We will be creative and use the logger filehandle to capture
>>> # what GnuPG says this time, instead stderr; no stdout to listen to,
>>> # but we capture logger to surpress the dry-run command.
>>> # We also have to capture stdout since otherwise doctest complains;
>>> # Normally you can let stdout through when generating a key.
>>> 
>>> proc = gnupg.run(['--gen-key'], create_fhs=['stdin', 'stdout',
...                                             'logger'])
>>> 
>>> proc.handles['stdin'].write('''Key-Type: DSA
... Key-Length: 1024
... # We are only testing syntax this time, so dry-run
... %dry-run
... Subkey-Type: ELG-E
... Subkey-Length: 1024
... Name-Real: Joe Tester
... Name-Comment: with stupid passphrase
... Name-Email: joe@foo.bar
... Expire-Date: 2y
... Passphrase: abc
... %pubring foo.pub
... %secring foo.sec
... ''')
>>> 
>>> proc.handles['stdin'].close()
>>> 
>>> report = proc.handles['logger'].read()
>>> proc.handles['logger'].close()
>>> 
>>> proc.wait()


COPYRIGHT:

Copyright (C) 2001  Frank J. Tobin, ftobin@neverending.org

LICENSE:

This library is free software; you can redistribute it and/or
modify it under the terms of the GNU Lesser General Public
License as published by the Free Software Foundation; either
version 2.1 of the License, or (at your option) any later version.

This library is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
Lesser General Public License for more details.

You should have received a copy of the GNU Lesser General Public
License along with this library; if not, write to the Free Software
Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
or see http://www.gnu.org/copyleft/lesser.html
"""

import os
import sys
import subprocess
# import fcntl

__author__   = "Frank J. Tobin, ftobin@neverending.org"
__version__  = "0.3.2"
__revision__ = "$Id: GnuPGInterface.py,v 1.22 2002/01/11 20:22:04 ftobin Exp $"

# "standard" filehandles attached to processes
_stds = [ 'stdin', 'stdout', 'stderr' ]

# the permissions each type of fh needs to be opened with
_fd_modes = { 'stdin':      'w',
              'stdout':     'r',
              'stderr':     'r',
              'passphrase': 'w',
              'command':    'w',
              'logger':     'r',
              'status':     'r'
              }

# correlation between handle names and the arguments we'll pass
_fd_options = { 'passphrase': '--passphrase-fd',
                'logger':     '--logger-fd',
                'status':     '--status-fd',
                'command':    '--command-fd' }

class GnuPG:
    """Class instances represent GnuPG.
    
    Instance attributes of a GnuPG object are:
    
    * call -- string to call GnuPG with.  Defaults to "gpg"

    * passphrase -- Since it is a common operation
      to pass in a passphrase to GnuPG,
      and working with the passphrase filehandle mechanism directly
      can be mundane, if set, the passphrase attribute
      works in a special manner.  If the passphrase attribute is set, 
      and no passphrase file object is sent in to run(),
      then GnuPG instnace will take care of sending the passphrase to
      GnuPG, the executable instead of having the user sent it in manually.
      
    * options -- Object of type GnuPGInterface.Options. 
      Attribute-setting in options determines
      the command-line options used when calling GnuPG.
    """

    def __init__(self):
        self.call = 'gpg'
        self.passphrase = None
        self.options = Options()
    
    def run(self, gnupg_commands, args=None, create_fhs=None, attach_fhs=None):
	"""Calls GnuPG with the list of string commands gnupg_commands,
	complete with prefixing dashes.
	For example, gnupg_commands could be
	'["--sign", "--encrypt"]'
	Returns a GnuPGInterface.Process object.
	
	args is an optional list of GnuPG command arguments (not options),
	such as keyID's to export, filenames to process, etc.

        create_fhs is an optional list of GnuPG filehandle
        names that will be set as keys of the returned Process object's
        'handles' attribute.  The generated filehandles can be used
        to communicate with GnuPG via standard input, standard output,
        the status-fd, passphrase-fd, etc.
        
        Valid GnuPG filehandle names are:
          * stdin
          * stdout
          * stderr
          * status
          * passphase
          * command
          * logger
        
        The purpose of each filehandle is described in the GnuPG
        documentation.
        
        attach_fhs is an optional dictionary with GnuPG filehandle
        names mapping to opened files.  GnuPG will read or write
        to the file accordingly.  For example, if 'my_file' is an
        opened file and 'attach_fhs[stdin] is my_file', then GnuPG
        will read its standard input from my_file. This is useful
        if you want GnuPG to read/write to/from an existing file.
	For instance:
        
	    f = open("encrypted.gpg")
            gnupg.run(["--decrypt"], attach_fhs={'stdin': f})

        Using attach_fhs also helps avoid system buffering
        issues that can arise when using create_fhs, which
        can cause the process to deadlock.
        
        If not mentioned in create_fhs or attach_fhs,
	GnuPG filehandles which are a std* (stdin, stdout, stderr)
        are defaulted to the running process' version of handle.
	Otherwise, that type of handle is simply not used when calling GnuPG.
	For example, if you do not care about getting data from GnuPG's
	status filehandle, simply do not specify it.
	
	run() returns a Process() object which has a 'handles'
        which is a dictionary mapping from the handle name
        (such as 'stdin' or 'stdout') to the respective
        newly-created FileObject connected to the running GnuPG process.
	For instance, if the call was

          process = gnupg.run(["--decrypt"], stdin=1)
          
	after run returns 'process.handles["stdin"]'
        is a FileObject connected to GnuPG's standard input,
	and can be written to.
        """
        
	if args == None: args = []
        if create_fhs == None: create_fhs = []
        if attach_fhs == None: attach_fhs = {}
	
        for std in _stds:
            if not attach_fhs.has_key(std) \
               and std not in create_fhs:
                attach_fhs.setdefault(std, getattr(sys, std))
        
        handle_passphrase = 0
        
        if self.passphrase != None \
           and not attach_fhs.has_key('passphrase') \
           and 'passphrase' not in create_fhs:
            handle_passphrase = 1
            create_fhs.append('passphrase')
        
        process = self._attach_fork_exec(gnupg_commands, args,
                                         create_fhs, attach_fhs)
        
        if handle_passphrase:
            passphrase_fh = process.handles['passphrase']
            passphrase_fh.write( self.passphrase )
            passphrase_fh.close()
            del process.handles['passphrase']
        
        return process
    
    
    def _attach_fork_exec(self, gnupg_commands, args, create_fhs, attach_fhs):
        """This is like run(), but without the passphrase-helping
	(note that run() calls this)."""
	
        process = Process()
        
        for fh_name in create_fhs + attach_fhs.keys():
            if not _fd_modes.has_key(fh_name):
                raise KeyError, \
                      "unrecognized filehandle name '%s'; must be one of %s" \
                      % (fh_name, _fd_modes.keys())

        for fh_name in create_fhs:
            # make sure the user doesn't specify a filehandle
            # to be created *and* attached
            if attach_fhs.has_key(fh_name):
                raise ValueError, \
                      "cannot have filehandle '%s' in both create_fhs and attach_fhs" \
                      % fh_name

        

        process_arg = self._as_child(process, gnupg_commands, args, attach_fhs)
        
        
        if not attach_fhs.has_key("stdin"):
            process.handles["stdin"] = process_arg.stdin
            process.closehandles["stdin"] = process_arg.stdin
        else:
            process.handles["stdin"] = attach_fhs["stdin"]
            
        if not attach_fhs.has_key("stdout"):
            process.handles["stdout"] = process_arg.stdout
            process.closehandles["stdout"] = process_arg.stdout
        else:
            process.handles["stdout"] = attach_fhs["stdout"]

        if not attach_fhs.has_key("stderr"):
            process.handles["stderr"] = process_arg.stderr
            process.closehandles["stderr"] = process_arg.stderr
        else:
            process.handles["stderr"] = attach_fhs["stderr"]

        process.handles["passphrase"] = FakeFile(process.handles["stdin"])
        
        process.args = process_arg
        process.pid = process_arg.pid
        return process
    
    
    def _as_parent(self, process):
        """Stuff run after forking in parent"""
        for k, p in process._pipes.items():
            if not p.direct:
                os.close(p.child)
                process.handles[k] = os.fdopen(p.parent, _fd_modes[k])
        
        # user doesn't need these
        #del process._pipes
        
        return process


    def _as_child(self, process, gnupg_commands, args, attach_fhs):
        """Stuff run after forking in child"""
        # child
       
        fd_args = []
        
        k = 'passphrase'
        fd_args.extend([ _fd_options[k], "%d" % 0 ])
        
        command = [ self.call ] + fd_args + self.options.get_args() \
                  + gnupg_commands + args
                  
        stdin = subprocess.PIPE
        stdout = subprocess.PIPE
        stderr = subprocess.PIPE

        if attach_fhs.has_key("stdin"):
            stdin = attach_fhs['stdin']

        if attach_fhs.has_key("stdout"):
            stdout = attach_fhs['stdout']

        if attach_fhs.has_key("stderr"):
            stderr = attach_fhs['stderr']
        
        #os.execvp( command[0], command )
        #return subprocess.Popen(command, 0, None, 4, 3, 5)
        #return subprocess.Popen(command, 0, None, stdin, stdout, stderr)
        return subprocess.Popen(command, 0, None, stdin, stdout, stderr)
        #return subprocess.Popen(command, 0, None, process._pipes['stdin'].child, process._pipes['stdout'].child, process._pipes['stderr'].child)

class FakeFile:
    """Simple wrapper for a file, where close is substituted with linefeed"""
    
    def __init__(self, file):
        self._file = file
        self._closed = 0
        
    def write(self, arg):
        if self._closed:
            raise ValueError, \
                      "handle is closed"
        self._file.write(arg)
    
    def close(self):
        self.write("\n")
        self._closed = 1
        
    
class Pipe:
    """simple struct holding stuff about pipes we use"""
    def __init__(self, parent, child, direct):
        self.parent = parent
        self.child = child
        self.direct = direct


class Options:
    """Objects of this class encompass options passed to GnuPG.
    This class is responsible for determining command-line arguments
    which are based on options.  It can be said that a GnuPG
    object has-a Options object in its options attribute.
    
    Attributes which correlate directly to GnuPG options:
    
    Each option here defaults to false or None, and is described in
    GnuPG documentation.
    
    Booleans (set these attributes to booleans)
    
      * armor
      * no_greeting
      * no_verbose
      * quiet
      * batch
      * always_trust
      * rfc1991
      * openpgp
      * force_v3_sigs
      * no_options
      * textmode
    
    Strings (set these attributes to strings)
    
      * homedir
      * default_key
      * comment
      * compress_algo
      * options
    
    Lists (set these attributes to lists)
    
      * recipients  (***NOTE*** plural of 'recipient')
      * encrypt_to
    
    Meta options
    
    Meta options are options provided by this module that do
    not correlate directly to any GnuPG option by name,
    but are rather bundle of options used to accomplish
    a specific goal, such as obtaining compatibility with PGP 5.
    The actual arguments each of these reflects may change with time.  Each
    defaults to false unless otherwise specified.
    
    meta_pgp_5_compatible -- If true, arguments are generated to try
    to be compatible with PGP 5.x.
      
    meta_pgp_2_compatible -- If true, arguments are generated to try
    to be compatible with PGP 2.x.
    
    meta_interactive -- If false, arguments are generated to try to
    help the using program use GnuPG in a non-interactive
    environment, such as CGI scripts.  Default is true.
    
    extra_args -- Extra option arguments may be passed in
    via the attribute extra_args, a list.

    >>> import GnuPGInterface
    >>> 
    >>> gnupg = GnuPGInterface.GnuPG()
    >>> gnupg.options.armor = 1
    >>> gnupg.options.recipients = ['Alice', 'Bob']
    >>> gnupg.options.extra_args = ['--no-secmem-warning']
    >>> 
    >>> # no need for users to call this normally; just for show here
    >>> gnupg.options.get_args()
    ['--armor', '--recipient', 'Alice', '--recipient', 'Bob', '--no-secmem-warning']
    """
    
    def __init__(self):
        # booleans
        self.armor = 0
        self.no_greeting = 0
        self.verbose = 0
        self.no_verbose = 0
        self.quiet = 0
        self.batch = 0
        self.always_trust = 0
        self.rfc1991 = 0
        self.openpgp = 0
        self.force_v3_sigs = 0
        self.no_options = 0
        self.textmode = 0

        # meta-option booleans
        self.meta_pgp_5_compatible = 0
        self.meta_pgp_2_compatible = 0
        self.meta_interactive = 1

        # strings
        self.homedir = None
        self.default_key = None
        self.comment = None
        self.compress_algo = None
        self.options = None

        # lists
        self.encrypt_to = []
        self.recipients = []
        
        # miscellaneous arguments
        self.extra_args = []
    
    def get_args( self ):
	"""Generate a list of GnuPG arguments based upon attributes."""
	
        return self.get_meta_args() + self.get_standard_args() + self.extra_args

    def get_standard_args( self ):
	"""Generate a list of standard, non-meta or extra arguments"""
        args = []
        if self.homedir != None: args.extend( [ '--homedir', self.homedir ] )
        if self.options != None: args.extend( [ '--options', self.options ] )
        if self.comment != None: args.extend( [ '--comment', self.comment ] )
        if self.compress_algo != None: args.extend( [ '--compress-algo', self.compress_algo ] )
        if self.default_key != None: args.extend( [ '--default-key', self.default_key ] )
        
        if self.no_options: args.append( '--no-options' )
        if self.armor: args.append( '--armor' )
        if self.textmode: args.append( '--textmode' )
        if self.no_greeting: args.append( '--no-greeting' )
        if self.verbose: args.append( '--verbose' )
        if self.no_verbose: args.append( '--no-verbose' )
        if self.quiet: args.append( '--quiet' )
        if self.batch: args.append( '--batch' )
        if self.always_trust: args.append( '--always-trust' )
        if self.force_v3_sigs: args.append( '--force-v3-sigs' )
        if self.rfc1991: args.append( '--rfc1991' )
        if self.openpgp: args.append( '--openpgp' )

        for r in self.recipients: args.extend( [ '--recipient',  r ] )
        for r in self.encrypt_to: args.extend( [ '--encrypt-to', r ] )
        
        return args

    def get_meta_args( self ):
	"""Get a list of generated meta-arguments"""
        args = []

        if self.meta_pgp_5_compatible: args.extend( [ '--compress-algo', '1',
                                                      '--force-v3-sigs'
                                                      ] )
        if self.meta_pgp_2_compatible: args.append( '--rfc1991' )
        if not self.meta_interactive: args.extend( [ '--batch', '--no-tty' ] )

        return args


class Process:
    """Objects of this class encompass properties of a GnuPG
    process spawned by GnuPG.run().
    
    # gnupg is a GnuPG object
    process = gnupg.run( [ '--decrypt' ], stdout = 1 )
    out = process.handles['stdout'].read()
    ...
    os.waitpid( process.pid, 0 )
    
    Data Attributes
    
    handles -- This is a map of filehandle-names to
    the file handles, if any, that were requested via run() and hence
    are connected to the running GnuPG process.  Valid names
    of this map are only those handles that were requested.
      
    pid -- The PID of the spawned GnuPG process.
    Useful to know, since once should call
    os.waitpid() to clean up the process, especially
    if multiple calls are made to run().
    """
    
    def __init__(self):
        self._pipes  = {}
        self.handles = {}
        self.pid     = None
        self._waited = None
        self.closehandles = {}

    def wait(self):
        """Wait on the process to exit, allowing for child cleanup.
        Will raise an IOError if the process exits non-zero."""
        
        e = self.args.wait()
        
        if self.handles.has_key('stdout'):
            if not self.handles['stdout'].closed:
                self.handles['stdout'].close()
                
        if e != 0:
            raise IOError, "GnuPG exited non-zero, with code %d" % (e << 8)

def _run_doctests():
    import doctest, GnuPGInterface
    
    return doctest.testmod(GnuPGInterface)

def _run_my_test():
    import GnuPGInterface, subprocess, os
    try:
#        pobj = subprocess.Popen(["C:\Programmer\GNU\GnuPG\gpg.exe"])
    
        plaintext  = "Three blind mice"
        passphrase = "This is the passphrase"
        
        gnupg = GnuPGInterface.GnuPG()
        gnupg.options.armor = 1
        gnupg.options.meta_interactive = 0
        gnupg.options.extra_args.append('--no-secmem-warning')
        
        p1 = gnupg.run(['--symmetric'], create_fhs=['stdin', 'stdout', 'passphrase'])
        p1.handles['passphrase'].write(passphrase)
        p1.handles['passphrase'].close()
        
        p1.handles['stdin'].write(plaintext)
        p1.handles['stdin'].close()
        
        ciphertext = p1.handles['stdout'].read()
        p1.handles['stdout'].close()
        
        p1.wait()
        
        gnupg.passphrase = passphrase
        p2 = gnupg.run(['--decrypt'], create_fhs=['stdin', 'stdout'])
        
        p2.handles['stdin'].write(ciphertext)
        p2.handles['stdin'].close()
        decrypted_plaintext = p2.handles['stdout'].read()
        p2.handles['stdout'].close()
        p2.wait()    
    except Exception, e:
        pobj = None

# deprecated
GnuPGInterface = GnuPG

if __name__ == '__main__':
    _run_my_test()
    _run_doctests()
