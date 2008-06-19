# Copyright (c) 2006,2007 Mitch Garnaat http://garnaat.org/
#
# Permission is hereby granted, free of charge, to any person obtaining a
# copy of this software and associated documentation files (the
# "Software"), to deal in the Software without restriction, including
# without limitation the rights to use, copy, modify, merge, publish, dis-
# tribute, sublicense, and/or sell copies of the Software, and to permit
# persons to whom the Software is furnished to do so, subject to the fol-
# lowing conditions:
#
# The above copyright notice and this permission notice shall be included
# in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
# OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABIL-
# ITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
# SHALL THE AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
# IN THE SOFTWARE.

#
# Parts of this code were copied or derived from sample code supplied by AWS.
# The following notice applies to that code.
#
#  This software code is made available "AS IS" without warranties of any
#  kind.  You may copy, display, modify and redistribute the software
#  code either by itself or as incorporated into your code; provided that
#  you do not remove any proprietary notices.  Your use of this software
#  code is at your own risk and you waive any claim against Amazon
#  Digital Services, Inc. or its affiliates with respect to your use of
#  this software code. (c) 2006 Amazon Digital Services, Inc. or its
#  affiliates.

"""
Some handy utility functions used by several classes.
"""

import base64
import hmac
import re
import sha
import urllib, urllib2
import imp
import popen2, os, StringIO
import time, datetime
import logging.handlers
import boto
import tempfile
try:
    import hashlib
    _hashfn = hashlib.sha512
except ImportError:
    import md5
    _hashfn = md5.md5

METADATA_PREFIX = 'x-amz-meta-'
AMAZON_HEADER_PREFIX = 'x-amz-'

# generates the aws canonical string for the given parameters
def canonical_string(method, path, headers, expires=None):
    interesting_headers = {}
    for key in headers:
        lk = key.lower()
        if lk in ['content-md5', 'content-type', 'date'] or lk.startswith(AMAZON_HEADER_PREFIX):
            interesting_headers[lk] = headers[key].strip()

    # these keys get empty strings if they don't exist
    if not interesting_headers.has_key('content-type'):
        interesting_headers['content-type'] = ''
    if not interesting_headers.has_key('content-md5'):
        interesting_headers['content-md5'] = ''

    # just in case someone used this.  it's not necessary in this lib.
    if interesting_headers.has_key('x-amz-date'):
        interesting_headers['date'] = ''

    # if you're using expires for query string auth, then it trumps date
    # (and x-amz-date)
    if expires:
        interesting_headers['date'] = str(expires)

    sorted_header_keys = interesting_headers.keys()
    sorted_header_keys.sort()

    buf = "%s\n" % method
    for key in sorted_header_keys:
        if key.startswith(AMAZON_HEADER_PREFIX):
            buf += "%s:%s\n" % (key, interesting_headers[key])
        else:
            buf += "%s\n" % interesting_headers[key]

    # don't include anything after the first ? in the resource...
    buf += "%s" % path.split('?')[0]

    # ...unless there is an acl or torrent parameter
    if re.search("[&?]acl($|=|&)", path):
        buf += "?acl"
    elif re.search("[&?]logging($|=|&)", path):
        buf += "?logging"
    elif re.search("[&?]torrent($|=|&)", path):
        buf += "?torrent"
    elif re.search("[&?]location($|=|&)", path):
        buf += "?location"

    return buf

def merge_meta(headers, metadata):
    final_headers = headers.copy()
    for k in metadata.keys():
        final_headers[METADATA_PREFIX + k] = metadata[k]

    return final_headers

def get_aws_metadata(headers):
    metadata = {}
    for hkey in headers.keys():
        if hkey.lower().startswith(METADATA_PREFIX):
            metadata[hkey[len(METADATA_PREFIX):]] = headers[hkey]
            del headers[hkey]
    return metadata

def retry_url(url):
    for i in range(0, 10):
        try:
            req = urllib2.Request(url)
            resp = urllib2.urlopen(req)
            return resp.read()
        except:
            boto.log.exception('Caught exception reading instance data')
            time.sleep(2**i)
    boto.log.error('Unable to read instance data, giving up')
    return ''
    
def get_instance_metadata(version='latest'):
    metadata = {}
    url = 'http://169.254.169.254/%s/meta-data/' % version
    data = retry_url(url)
    if data:
        md_fields = data.split('\n')
        for md in md_fields:
            val = retry_url(url + md)
            if val.find('\n') > 0:
                val = val.split('\n')
            metadata[md] = val
    return metadata

def get_instance_userdata(version='latest', sep=None):
    url = 'http://169.254.169.254/%s/user-data/' % version
    user_data = retry_url(url)
    if user_data:
        if sep:
            l = user_data.split(sep)
            user_data = {}
            for nvpair in l:
                t = nvpair.split('=')
                user_data[t[0].strip()] = t[1].strip()
    return user_data

ISO8601 = '%Y-%m-%dT%H:%M:%SZ'
    
def get_ts(ts=None):
    if not ts:
        ts = time.gmtime()
    return time.strftime(ISO8601, ts)

def parse_ts(ts):
    return datetime.datetime.strptime(ts, ISO8601)

def find_class(module_name, class_name=None):
    if class_name:
        module_name = "%s.%s" % (module_name, class_name)
    modules = module_name.split('.')
    path = None
    c = None

    for m in modules[1:]:
        if c:
            c = getattr(c, m)
        else:
            c = getattr(__import__(".".join(modules[0:-1])), m)
    return c
    
def update_dme(username, password, dme_id, ip_address):
    """
    Update your Dynamic DNS record with DNSMadeEasy.com
    """
    dme_url = 'https://www.dnsmadeeasy.com/servlet/updateip'
    dme_url += '?username=%s&password=%s&id=%s&ip=%s'
    s = urllib2.urlopen(dme_url % (username, password, dme_id, ip_address))
    return s.read()

def fetch_file(uri, file=None, username=None, password=None):
    """
    Fetch a file based on the URI provided. If you do not pass in a file pointer
    a tempfile.NamedTemporaryFile, or None if the file could not be 
    retrieved is returned.
    The URI can be either an HTTP url, or "s3://bucket_name/key_name"
    """
    boto.log.info('Fetching %s' % uri)
    if file == None:
        file = tempfile.NamedTemporaryFile()
    try:
        working_dir = boto.config.get("General", "working_dir")
        if uri.startswith('s3://'):
            bucket_name, key_name = uri[len('s3://'):].split('/')
            c = boto.connect_s3()
            bucket = c.get_bucket(bucket_name)
            key = bucket.get_key(key_name)
            key.get_contents_to_file(file)
        else:
            if username and password:
                passman = urllib2.HTTPPasswordMgrWithDefaultRealm()
                passman.add_password(None, uri, username, password)
                authhandler = urllib2.HTTPBasicAuthHandler(passman)
                opener = urllib2.build_opener(authhandler)
                urllib2.install_opener(opener)
            s = urllib2.urlopen(uri)
            file.write(s.read())
        file.seek(0)
    except:
        raise
        boto.log.exception('Problem Retrieving file: %s' % uri)
        file = None
    return file

class ShellCommand(object):

    def __init__(self, command, wait=True):
        self.exit_code = 0
        self.command = command
        self.log_fp = StringIO.StringIO()
        self.wait = wait
        self.run()

    def run(self):
        boto.log.info('running:%s' % self.command)
        self.process = popen2.Popen4(self.command)
        if(self.wait):
            return self.getStatus()

    def setReadOnly(self, value):
        raise AttributeError

    def getStatus(self):
        if(not self.exit_code):
            status = self.process.wait()
            self.log_fp.write(self.process.fromchild.read())
            boto.log.info(self.log_fp.getvalue())
            self.exit_code = os.WEXITSTATUS(status)
        return self.exit_code

    status = property(getStatus, setReadOnly, None, 'The exit code for the command')

    def getOutput(self):
        return self.log_fp.getvalue()

    output = property(getOutput, setReadOnly, None, 'The STDIN and STDERR output of the command')

class AuthSMTPHandler(logging.handlers.SMTPHandler):
    """
    This class extends the SMTPHandler in the standard Python logging module
    to accept a username and password on the constructor and to then use those
    credentials to authenticate with the SMTP server.  To use this, you could
    add something like this in your boto config file:
    
    [handler_hand07]
    class=boto.utils.AuthSMTPHandler
    level=WARN
    formatter=form07
    args=('localhost', 'username', 'password', 'from@abc', ['user1@abc', 'user2@xyz'], 'Logger Subject')
    """

    def __init__(self, mailhost, username, password, fromaddr, toaddrs, subject):
        """
        Initialize the handler.

        We have extended the constructor to accept a username/password
        for SMTP authentication.
        """
        logging.handlers.SMTPHandler.__init__(self, mailhost, fromaddr, toaddrs, subject)
        self.username = username
        self.password = password
        
    def emit(self, record):
        """
        Emit a record.

        Format the record and send it to the specified addressees.
        It would be really nice if I could add authorization to this class
        without having to resort to cut and paste inheritance but, no.
        """
        try:
            import smtplib
            try:
                from email.Utils import formatdate
            except:
                formatdate = self.date_time
            port = self.mailport
            if not port:
                port = smtplib.SMTP_PORT
            smtp = smtplib.SMTP(self.mailhost, port)
            smtp.login(self.username, self.password)
            msg = self.format(record)
            msg = "From: %s\r\nTo: %s\r\nSubject: %s\r\nDate: %s\r\n\r\n%s" % (
                            self.fromaddr,
                            string.join(self.toaddrs, ","),
                            self.getSubject(record),
                            formatdate(), msg)
            smtp.sendmail(self.fromaddr, self.toaddrs, msg)
            smtp.quit()
        except (KeyboardInterrupt, SystemExit):
            raise
        except:
            self.handleError(record)

class LRUCache(dict):
    """A dictionary-like object that stores only a certain number of items, and
    discards its least recently used item when full.
    
    >>> cache = LRUCache(3)
    >>> cache['A'] = 0
    >>> cache['B'] = 1
    >>> cache['C'] = 2
    >>> len(cache)
    3
    
    >>> cache['A']
    0
    
    Adding new items to the cache does not increase its size. Instead, the least
    recently used item is dropped:
    
    >>> cache['D'] = 3
    >>> len(cache)
    3
    >>> 'B' in cache
    False
    
    Iterating over the cache returns the keys, starting with the most recently
    used:
    
    >>> for key in cache:
    ...     print key
    D
    A
    C

    This code is based on the LRUCache class from Genshi which is based on
    Mighty's LRUCache from ``myghtyutils.util``, written
    by Mike Bayer and released under the MIT license (Genshi uses the
    BSD License). See:

      http://svn.myghty.org/myghtyutils/trunk/lib/myghtyutils/util.py
    """

    class _Item(object):
        def __init__(self, key, value):
            self.previous = self.next = None
            self.key = key
            self.value = value
        def __repr__(self):
            return repr(self.value)

    def __init__(self, capacity):
        self._dict = dict()
        self.capacity = capacity
        self.head = None
        self.tail = None

    def __contains__(self, key):
        return key in self._dict

    def __iter__(self):
        cur = self.head
        while cur:
            yield cur.key
            cur = cur.next

    def __len__(self):
        return len(self._dict)

    def __getitem__(self, key):
        item = self._dict[key]
        self._update_item(item)
        return item.value

    def __setitem__(self, key, value):
        item = self._dict.get(key)
        if item is None:
            item = self._Item(key, value)
            self._dict[key] = item
            self._insert_item(item)
        else:
            item.value = value
            self._update_item(item)
            self._manage_size()

    def __repr__(self):
        return repr(self._dict)

    def _insert_item(self, item):
        item.previous = None
        item.next = self.head
        if self.head is not None:
            self.head.previous = item
        else:
            self.tail = item
        self.head = item
        self._manage_size()

    def _manage_size(self):
        while len(self._dict) > self.capacity:
            olditem = self._dict[self.tail.key]
            del self._dict[self.tail.key]
            if self.tail != self.head:
                self.tail = self.tail.previous
                self.tail.next = None
            else:
                self.head = self.tail = None

    def _update_item(self, item):
        if self.head == item:
            return

        previous = item.previous
        previous.next = item.next
        if item.next is not None:
            item.next.previous = previous
        else:
            self.tail = previous

        item.previous = None
        item.next = self.head
        self.head.previous = self.head = item

class Password:
    """
    Password object that stores itself as SHA512 hashed.
    """
    def __init__(self, str=None):
        """
        Load the string from an initial value, this should be the raw SHA512 hashed password
        """
        self.str = str

    def set(self, value):
        self.str = _hashfn(value).hexdigest()
   
    def __str__(self):
        return str(self.str)
   
    def __eq__(self, other):
        return str(_hashfn(other).hexdigest()) == str(self.str)

    def __len__(self):
        return len(self.str)
