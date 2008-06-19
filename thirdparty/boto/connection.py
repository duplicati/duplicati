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
Handles basic connections to AWS
"""

import base64
import hmac
import httplib
import socket, errno
import re
import sha
import sys
import time
import urllib, urlparse
import os
import xml.sax
import boto
from boto.exception import AWSConnectionError, BotoClientError, BotoServerError
from boto.resultset import ResultSet
import boto.utils
from boto import config, UserAgent, handler

PORTS_BY_SECURITY = { True: 443, False: 80 }

class AWSAuthConnection:
    def __init__(self, server, aws_access_key_id=None,
                 aws_secret_access_key=None, is_secure=True, port=None,
                 proxy=None, proxy_port=None, proxy_user=None,
                 proxy_pass=None, debug=0, https_connection_factory=None):
        """
        @type server: string
        @param server: The server to make the connection to
        
        @type aws_access_key_id: string
        @param aws_access_key_id: AWS Access Key ID (provided by Amazon)
        
        @type aws_secret_access_key: string
        @param aws_secret_access_key: Secret Access Key (provided by Amazon)
        
        @type is_secure: boolean
        @param is_secure: Whether the connection is over SSL
        
        @type https_connection_factory: list or tuple
        @param https_connection_factory: A pair of an HTTP connection factory and the exceptions to catch. The factory should have a similar interface to L{httplib.HTTPSConnection}.
        
        @type proxy:
        @param proxy:
        
        @type proxy_port: int
        @param proxy_port: The port to use when connecting over a proxy
        
        @type proxy_user: string
        @param proxy_user: The username to connect with on the proxy
        
        @type proxy_pass: string
        @param proxy_pass: The password to use when connection over a proxy.
        
        @type port: integer
        @param port: The port to use to connect
        """
        
        self.num_retries = 5
        self.is_secure = is_secure
        self.handle_proxy(proxy, proxy_port, proxy_user, proxy_pass)
        # define exceptions from httplib that we want to catch and retry
        self.http_exceptions = (httplib.HTTPException, socket.error, socket.gaierror)
        # define values in socket exceptions we don't want to catch
        self.socket_exception_values = (errno.EINTR,)
        if https_connection_factory is not None:
            self.https_connection_factory = https_connection_factory[0]
            self.http_exceptions += https_connection_factory[1]
        else:
            self.https_connection_factory = None
        if (is_secure):
            self.protocol = 'https'
        else:
            self.protocol = 'http'
        self.server = server
        if debug:
            self.debug = debug
        else:
            self.debug = config.getint('Boto', 'debug', debug)
        if port:
            self.port = port
        else:
            self.port = PORTS_BY_SECURITY[is_secure]
        self.server_name = '%s:%d' % (server, self.port)
        
        if aws_access_key_id:
            self.aws_access_key_id = aws_access_key_id
        elif os.environ.has_key('AWS_ACCESS_KEY_ID'):
            self.aws_access_key_id = os.environ['AWS_ACCESS_KEY_ID']
        elif config.has_option('Credentials', 'aws_access_key_id'):
            self.aws_access_key_id = config.get('Credentials', 'aws_access_key_id')
        
        if aws_secret_access_key:
            self.aws_secret_access_key = aws_secret_access_key
        elif os.environ.has_key('AWS_SECRET_ACCESS_KEY'):
            self.aws_secret_access_key = os.environ['AWS_SECRET_ACCESS_KEY']
        elif config.has_option('Credentials', 'aws_secret_access_key'):
            self.aws_secret_access_key = config.get('Credentials', 'aws_secret_access_key')

        # initialize an HMAC for signatures, make copies with each request
        self.hmac = hmac.new(key=self.aws_secret_access_key, digestmod=sha)

        # cache up to 20 connections
        self._cache = boto.utils.LRUCache(20)
        self.refresh_http_connection(self.server, self.is_secure)
        self._last_rs = None

    def handle_proxy(self, proxy, proxy_port, proxy_user, proxy_pass):
        self.proxy = proxy
        self.proxy_port = proxy_port
        self.proxy_user = proxy_user
        self.proxy_pass = proxy_pass
        if os.environ.has_key('http_proxy') and not self.proxy:
            pattern = re.compile(
                '(?:http://)?' \
                '(?:(?P<user>\w+):(?P<pass>.*)@)?' \
                '(?P<host>[\w\-\.]+)' \
                '(?::(?P<port>\d+))?'
            )
            match = pattern.match(os.environ['http_proxy'])
            if match:
                self.proxy = match.group('host')
                self.proxy_port = match.group('port')
                self.proxy_user = match.group('user')
                self.proxy_pass = match.group('pass')
        else:
            if not self.proxy:
                self.proxy = config.get_value('Boto', 'proxy', None)
            if not self.proxy_port:
                self.proxy_port = config.get_value('Boto', 'proxy_port', None)
            if not self.proxy_user:
                self.proxy_user = config.get_value('Boto', 'proxy_user', None)
            if not self.proxy_pass:
                self.proxy_pass = config.get_value('Boto', 'proxy_pass', None)

        if not self.proxy_port and self.proxy:
            print "http_proxy environment variable does not specify " \
                "a port, using default"
            self.proxy_port = self.port
        self.use_proxy = (self.proxy != None)
        if self.use_proxy and self.is_secure:
            raise AWSConnectionError("Unable to provide secure connection through proxy")

    def get_http_connection(self, host, is_secure):
        if host is None:
            host = '%s:%d' % (self.server, self.port)
        cached_name = is_secure and 'https://' or 'http://'
        cached_name += host
        if cached_name in self._cache:
            return self._cache[cached_name]
        return self.refresh_http_connection(host, is_secure)

    def refresh_http_connection(self, host, is_secure):
        if self.use_proxy:
            host = '%s:%d' % (self.proxy, int(self.proxy_port))
        if host is None:
            host = '%s:%d' % (self.server, self.port)
        boto.log.debug('establishing HTTP connection')
        if is_secure:
            if self.https_connection_factory:
                connection = self.https_connection_factory(host)
            else:
                connection = httplib.HTTPSConnection(host)
        else:
            connection = httplib.HTTPConnection(host)
        if self.debug > 1:
            connection.set_debuglevel(self.debug)
        cached_name = is_secure and 'https://' or 'http://'
        cached_name += host
        if cached_name in self._cache:
            boto.log.debug('closing old HTTP connection')
            self._cache[cached_name].close()
        self._cache[cached_name] = connection
        # update self.connection for backwards-compatibility
        if host.split(':')[0] == self.server and is_secure == self.is_secure:
            self.connection = connection
        return connection

    def prefix_proxy_to_path(self, path, host=None):
        path = self.protocol + '://' + (host or self.server) + path
        return path

    def get_proxy_auth_header(self):
        auth = base64.encodestring(self.proxy_user+':'+self.proxy_pass)
        return {'Proxy-Authorization': 'Basic %s' % auth}

    def _mexe(self, method, path, data, headers, host=None, sender=None):
        """
        mexe - Multi-execute inside a loop, retrying multiple times to handle
               transient Internet errors by simply trying again.
               Also handles redirects.

        This code was inspired by the S3Utils classes posted to the boto-users
        Google group by Larry Bates.  Thanks!
        """
        boto.log.debug('Method: %s' % method)
        boto.log.debug('Path: %s' % path)
        boto.log.debug('Data: %s' % data)
        boto.log.debug('Headers: %s' % headers)
        boto.log.debug('Host: %s' % host)
        response = None
        body = None
        e = None
        num_retries = config.getint('Boto', 'num_retries', self.num_retries)
        i = 0
        connection = self.get_http_connection(host, self.is_secure)
        while i <= num_retries:
            try:
                if callable(sender):
                    response = sender(connection, method, path, data, headers)
                else:
                    connection.request(method, path, data, headers)
                    response = connection.getresponse()
                location = response.getheader('location')
                if response.status == 500 or response.status == 503:
                    boto.log.debug('received %d response, retrying in %d seconds' % (response.status, 2**i))
                    body = response.read()
                elif response.status == 408:
                    body = response.read()
                    print '-------------------------'
                    print '         4 0 8           '
                    print 'path=%s' % path
                    print body
                    print '-------------------------'
                elif response.status < 300 or response.status >= 400 or \
                        not location:
                    return response
                else:
                    scheme, host, path, params, query, fragment = \
                            urlparse.urlparse(location)
                    if query:
                        path += '?' + query
                    boto.log.debug('Redirecting: %s' % scheme + '://' + host + path)
                    connection = self.get_http_connection(host,
                            scheme == 'https')
                    continue
            except KeyboardInterrupt:
                sys.exit('Keyboard Interrupt')
            except self.http_exceptions, e:
                boto.log.debug('encountered %s exception, reconnecting' % \
                                  e.__class__.__name__)
                connection = self.refresh_http_connection(host, self.is_secure)
            time.sleep(2**i)
            i += 1
        # If we made it here, it's because we have exhausted our retries and stil haven't
        # succeeded.  So, if we have a response object, use it to raise an exception.
        # Otherwise, raise the exception that must have already happened.
        if response:
            raise BotoServerError(response.status, response.reason, body)
        elif e:
            raise e
        else:
            raise BotoClientError('Please report this exception as a Boto Issue!')

    def make_request(self, method, path, headers=None, data='', host=None,
            auth_path=None, sender=None):
        if headers == None:
            headers = {'User-Agent' : UserAgent}
        else:
            headers = headers.copy()
        if not headers.has_key('Content-Length'):
            headers['Content-Length'] = len(data)
        if self.use_proxy:
            path = self.prefix_proxy_to_path(path, host)
            if self.proxy_user and self.proxy_pass:
                headers.update(self.get_proxy_auth_header())
        self.add_aws_auth_header(headers, method, auth_path or path)
        return self._mexe(method, path, data, headers, host, sender)

    def add_aws_auth_header(self, headers, method, path):
        if not headers.has_key('Date'):
            headers['Date'] = time.strftime("%a, %d %b %Y %H:%M:%S GMT",
                                            time.gmtime())

        c_string = boto.utils.canonical_string(method, path, headers)
        boto.log.debug('Canonical: %s' % c_string)
        hmac = self.hmac.copy()
        hmac.update(c_string)
        b64_hmac = base64.encodestring(hmac.digest()).strip()
        headers['Authorization'] = "AWS %s:%s" % (self.aws_access_key_id, b64_hmac)

class AWSQueryConnection(AWSAuthConnection):

    APIVersion = ''
    SignatureVersion = '1'
    ResponseError = BotoServerError

    def __init__(self, aws_access_key_id=None, aws_secret_access_key=None,
                 is_secure=True, port=None, proxy=None, proxy_port=None,
                 proxy_user=None, proxy_pass=None, host=None, debug=0,
                 https_connection_factory=None):
        AWSAuthConnection.__init__(self, host, aws_access_key_id, aws_secret_access_key,
                                   is_secure, port, proxy, proxy_port, proxy_user, proxy_pass,
                                   debug,  https_connection_factory)

    def calc_signature_0(self, params):
        boto.log.debug('using calc_signature_0')
        hmac = self.hmac.copy()
        s = params['Action'] + params['Timestamp']
        hmac.update(s)
        keys = params.keys()
        keys.sort(cmp = lambda x, y: cmp(x.lower(), y.lower()))
        qs = ''
        for key in keys:
            qs += key + '=' + urllib.quote(unicode(params[key]).encode('utf-8')) + '&'
        return (qs, base64.b64encode(hmac.digest()))

    def calc_signature_1(self, params):
        boto.log.debug('using calc_signature_1')
        hmac = self.hmac.copy()
        keys = params.keys()
        keys.sort(cmp = lambda x, y: cmp(x.lower(), y.lower()))
        qs = ''
        for key in keys:
            hmac.update(key)
            val = params[key]
            if not isinstance(val, str) and not isinstance(val, unicode):
                val = str(val)
            hmac.update(val)
            qs += key + '=' + urllib.quote(unicode(params[key]).encode('utf-8')) + '&'
        return (qs, base64.b64encode(hmac.digest()))

    def get_signature(self, params):
        if self.SignatureVersion == '1':
            t = self.calc_signature_1(params)
        elif self.SignatureVersion == '0':
            t = self.calc_signature_0(params)
        else:
            raise BotoClientError('Unknown Signature Version: %s' % self.SignatureVersion)
        return t

    def make_request(self, action, params=None, path=None, verb='GET'):
        headers = {'User-Agent' : UserAgent}
        if path == None:
            path = '/'
        if params == None:
            params = {}
        params['Action'] = action
        params['Version'] = self.APIVersion
        params['AWSAccessKeyId'] = self.aws_access_key_id
        params['SignatureVersion'] = self.SignatureVersion
        params['Timestamp'] = time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime())
        qs, signature = self.get_signature(params)
        qs = path + '?' + qs + 'Signature=' + urllib.quote(signature)
        if self.use_proxy:
            qs = self.prefix_proxy_to_path(qs)
        return self._mexe(verb, qs, None, headers)

    def build_list_params(self, params, items, label):
        if isinstance(items, str):
            items = [items]
        for i in range(1, len(items)+1):
            params['%s.%d' % (label, i)] = items[i-1]
            
    # generics

    def get_list(self, action, params, markers, path='/', parent=None):
        if not parent:
            parent = self
        response = self.make_request(action, params, path)
        body = response.read()
        if response.status == 200:
            rs = ResultSet(markers)
            h = handler.XmlHandler(rs, parent)
            xml.sax.parseString(body, h)
            return rs
        else:
            boto.log.error('%s %s' % (response.status, response.reason))
            boto.log.error('%s' % body)
            raise self.ResponseError(response.status, response.reason, body)
        
    def get_object(self, action, params, cls, path='/', parent=None):
        if not parent:
            parent = self
        response = self.make_request(action, params, path)
        body = response.read()
        if response.status == 200:
            obj = cls(parent)
            h = handler.XmlHandler(obj, parent)
            xml.sax.parseString(body, h)
            return obj
        else:
            boto.log.error('%s %s' % (response.status, response.reason))
            boto.log.error('%s' % body)
            raise self.ResponseError(response.status, response.reason, body)
        
    def get_status(self, action, params, path='/', parent=None):
        if not parent:
            parent = self
        response = self.make_request(action, params, path)
        body = response.read()
        if response.status == 200:
            rs = ResultSet()
            h = handler.XmlHandler(rs, parent)
            xml.sax.parseString(body, h)
            return rs.status
        else:
            boto.log.error('%s %s' % (response.status, response.reason))
            boto.log.error('%s' % body)
            raise self.ResponseError(response.status, response.reason, body)

