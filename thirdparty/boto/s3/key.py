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

import urllib
import socket
import mimetypes
import md5
import os
import rfc822
import StringIO
import time
import base64
import boto
import boto.utils
from boto.exception import S3ResponseError, S3DataError, BotoClientError
from boto.s3.user import User
from boto import UserAgent, config

class Key:

    DefaultContentType = 'application/octet-stream'

    BufferSize = 8192

    def __init__(self, bucket=None, name=None):
        self.bucket = bucket
        self.name = name
        self.metadata = {}
        self.content_type = self.DefaultContentType
        self.filename = None
        self.etag = None
        self.last_modified = None
        self.owner = None
        self.storage_class = None
        self.md5 = None
        self.base64md5 = None
        self.path = None
        self.resp = None
        self.mode = None
        self.size = None

    def __repr__(self):
        if self.bucket:
            return '<Key: %s,%s>' % (self.bucket.name, self.name)
        else:
            return '<Key: None,%s>' % self.name

    def __getattr__(self, name):
        if name == 'key':
            return self.name
        else:
            raise AttributeError

    def __setattr__(self, name, value):
        if name == 'key':
            self.__dict__['name'] = value
        else:
            self.__dict__[name] = value

    def __iter__(self):
        return self

    def open_read(self, headers=None, query_args=None):
        """
        Open this key for reading
        
        @type headers: dict
        @param headers: Headers to pass in the web request
        
        @type query_args: string
        @param query_args: Arguments to pass in the query string (ie, 'torrent')
        """
        if self.resp == None:
            self.mode = 'r'
            
            self.resp = self.bucket.connection.make_request('GET', self.bucket.name, self.name, headers, query_args=query_args)
            if self.resp.status < 199 or self.resp.status > 299:
                raise S3ResponseError(self.resp.status, self.resp.reason)
            response_headers = self.resp.msg
            self.metadata = boto.utils.get_aws_metadata(response_headers)
            for name,value in response_headers.items():
                if name.lower() == 'content-length':
                    self.size = int(value)
                elif name.lower() == 'etag':
                    self.etag = value
                elif name.lower() == 'content-type':
                    self.content_type = value
                elif name.lower() == 'last-modified':
                    self.last_modified = value

    def open_write(self, headers=None):
        """
        Open this key for writing. 
        Not yet implemented
        
        @type headers: dict
        @param headers: Headers to pass in the write request
        """
        raise BotoClientError('Not Implemented')

    def open(self, mode='r', headers=None, query_args=None):
        if mode == 'r':
            self.mode = 'r'
            self.open_read(headers=headers, query_args=query_args)
        elif mode == 'w':
            self.mode = 'w'
            self.open_write(headers=headers)
        else:
            raise BotoClientError('Invalid mode: %s' % mode)

    def close(self):
        if self.resp:
            self.resp.read()
        self.resp = None
        self.mode = None
    
    def next(self):
        """
        By providing a next method, the key object supports use as an iterator.
        For example, you can now say:

        for bytes in key:
            write bytes to a file or whatever

        All of the HTTP connection stuff is handled for you.
        """
        self.open_read()
        data = self.resp.read(self.BufferSize)
        if not data:
            self.close()
            raise StopIteration
        return data

    def read(self, size=0):
        if size == 0:
            size = self.BufferSize
        self.open_read()
        data = self.resp.read(size)
        if not data:
            self.close()
        return data

    def copy(self, dst_bucket, dst_key, metadata=None):
        """
        Copy this Key to another bucket.

        @type dst_bucket: string
        @param dst_bucket: The name of the destination bucket

        @type dst_key: string
        @param dst_key: The name of the destinatino key
        
        @type metadata: dict
        @param metadata: Metadata to be associated with new key.
                         If metadata is supplied, it will replace the
                         metadata of the source key being copied.
                         If no metadata is supplied, the source key's
                         metadata will be copied to the new key.

        @rtype: L{Key<boto.s3.key.Key>} or subclass
        @returns: An instance of the newly created key object
        """
        dst_bucket = self.bucket.connection.lookup(dst_bucket)
        return dst_bucket.copy_key(dst_key, self.bucket.name, self.name, metadata)

    def startElement(self, name, attrs, connection):
        if name == 'Owner':
            self.owner = User(self)
            return self.owner
        else:
            return None

    def endElement(self, name, value, connection):
        if name == 'Key':
            self.name = value.encode('utf-8')
        elif name == 'ETag':
            self.etag = value
        elif name == 'LastModified':
            self.last_modified = value
        elif name == 'Size':
            self.size = int(value)
        elif name == 'StorageClass':
            self.storage_class = value
        elif name == 'Owner':
            pass
        else:
            setattr(self, name, value)

    def exists(self):
        """
        Returns True if the key exists
        
        @rtype: bool
        @return: Whether the key exists on S3
        """
        return bool(self.bucket.lookup(self.name))

    def delete(self):
        """
        Delete this key from S3
        """
        return self.bucket.delete_key(self.name)

    def get_metadata(self, name):
        return self.metadata.get(name)

    def set_metadata(self, name, value):
        self.metadata[name] = value

    def update_metadata(self, d):
        self.metadata.update(d)
    
    # convenience methods for setting/getting ACL
    def set_acl(self, acl_str):
        if self.bucket != None:
            self.bucket.set_acl(acl_str, self.name)

    def get_acl(self):
        if self.bucket != None:
            return self.bucket.get_acl(self.name)

    def get_xml_acl(self):
        if self.bucket != None:
            return self.bucket.get_xml_acl(self.name)

    def set_xml_acl(self, acl_str):
        if self.bucket != None:
            return self.bucket.set_xml_acl(acl_str, self.name)
        
    def make_public(self):
        response = self.bucket.connection.make_request('PUT', self.bucket.name, self.name,
                headers={'x-amz-acl': 'public-read'}, query_args='acl')
        body = response.read()
        if response.status != 200:
            raise S3ResponseError(response.status, response.reason, body)

    def generate_url(self, expires_in, method='GET', headers=None, query_auth=True):
        """
        Generate a URL to access this key.
        
        @type expires_in: int
        @param expires_in: How long the url is valid for
        
        @type method: string
        @param method: The method to use for retrieving the file (default is GET)
        
        @type headers: dict
        @param headers: Any headers to pass along in the request
        
        @type query_auth: bool
        @param query_auth: 
        
        @rtype: string
        @return: The URL to access the key
        """
        return self.bucket.connection.generate_url(expires_in, method,
                                                   self.bucket.name, self.name,
                                                   headers, query_auth)

    def send_file(self, fp, headers=None, cb=None, num_cb=10):
        """
        Upload a file to a key into a bucket on S3.
        
        @type fp: file
        @param fp: The file pointer to upload
        
        @type headers: dict
        @param headers: The headers to pass along with the PUT request
        
        @type cb: function
        @param cb: a callback function that will be called to report
                    progress on the upload.  The callback should accept two integer
                    parameters, the first representing the number of bytes that have
                    been successfully transmitted to S3 and the second representing
                    the total number of bytes that need to be transmitted.
                    
        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.  
             
        """
        def sender(http_conn, method, path, data, headers):
            http_conn.putrequest('PUT', path)
            for key in headers:
                http_conn.putheader(key, headers[key])
            http_conn.endheaders()
            fp.seek(0)
            save_debug = self.bucket.connection.debug
            self.bucket.connection.debug = 0
            if cb:
                if num_cb > 2:
                    cb_count = self.size / self.BufferSize / (num_cb-2)
                else:
                    cb_count = 0
                i = total_bytes = 0
                cb(total_bytes, self.size)
            l = fp.read(self.BufferSize)
            while len(l) > 0:
                http_conn.send(l)
                if cb:
                    total_bytes += len(l)
                    i += 1
                    if i == cb_count:
                        cb(total_bytes, self.size)
                        i = 0
                l = fp.read(self.BufferSize)
            if cb:
                cb(total_bytes, self.size)
            response = http_conn.getresponse()
            body = response.read()
            fp.seek(0)
            self.bucket.connection.debug = save_debug
            if response.status == 500 or response.status == 503 or \
                    response.getheader('location'):
                # we'll try again
                return response
            elif response.status >= 200 and response.status <= 299:
                self.etag = response.getheader('etag')
                if self.etag != '"%s"'  % self.md5:
                    raise S3DataError('ETag from S3 did not match computed MD5')
                return response
            else:
                raise S3ResponseError(response.status, response.reason, body)

        if not headers:
            headers = {}
        else:
            headers = headers.copy()
        headers['User-Agent'] = UserAgent
        headers['Content-MD5'] = self.base64md5
        if headers.has_key('Content-Type'):
            self.content_type = headers['Content-Type']
        elif self.path:
            self.content_type = mimetypes.guess_type(self.path)[0]
            if self.content_type == None:
                self.content_type = self.DefaultContentType
            headers['Content-Type'] = self.content_type
        else:
            headers['Content-Type'] = self.content_type
        headers['Content-Length'] = self.size
        headers['Expect'] = '100-Continue'
        headers = boto.utils.merge_meta(headers, self.metadata)
        return self.bucket.connection.make_request('PUT', self.bucket.name,
                self.name, headers, sender=sender)

    def _compute_md5(self, fp):
        """
        @type fp: file
        @param fp: File pointer to the file to MD5 hash
        
        @rtype: string
        @return: MD5 Hash of the file in fp
        """
        m = md5.new()
        s = fp.read(self.BufferSize)
        while s:
            m.update(s)
            s = fp.read(self.BufferSize)
        self.md5 = m.hexdigest()
        self.base64md5 = base64.encodestring(m.digest())
        if self.base64md5[-1] == '\n':
            self.base64md5 = self.base64md5[0:-1]
        self.size = fp.tell()
        fp.seek(0)

    def set_contents_from_file(self, fp, headers=None, replace=True, cb=None, num_cb=10, policy=None):
        """
        Store an object in S3 using the name of the Key object as the
        key in S3 and the contents of the file pointed to by 'fp' as the
        contents.
        
        @type fp: file
        @param fp: the file whose contents to upload
        
        @type headers: dict
        @param headers: additional HTTP headers that will be sent with the PUT request.

        @type replace: bool
        @param replace: If this parameter is False, the method
                        will first check to see if an object exists in the
                        bucket with the same key.  If it does, it won't
                        overwrite it.  The default value is True which will
                        overwrite the object.
                    
        @type cb: function
        @param cb: a callback function that will be called to report
                    progress on the upload.  The callback should accept two integer
                    parameters, the first representing the number of bytes that have
                    been successfully transmitted to S3 and the second representing
                    the total number of bytes that need to be transmitted.
                    
        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.

        @type policy: L{CannedACLString<boto.s3.acl.CannedACLStrings>}
        @param policy: A canned ACL policy that will be applied to the new key in S3.
             
        """
        if policy:
            if headers:
                headers['x-amz-acl'] = policy
            else:
                headers = {'x-amz-acl' : policy}
        if hasattr(fp, 'name'):
            self.path = fp.name
        if self.bucket != None:
            self._compute_md5(fp)
            if self.name == None:
                self.name = self.md5
            if not replace:
                k = self.bucket.lookup(self.name)
                if k:
                    return
            self.send_file(fp, headers, cb, num_cb)

    def set_contents_from_filename(self, filename, headers=None, replace=True, cb=None, num_cb=10, policy=None):
        """
        Store an object in S3 using the name of the Key object as the
        key in S3 and the contents of the file named by 'filename'.
        See set_contents_from_file method for details about the
        parameters.
        
        @type filename: string
        @param filename: The name of the file that you want to put onto S3
        
        @type headers: dict
        @param headers: Additional headers to pass along with the request to AWS.
        
        @type replace: bool
        @param replace: If True, replaces the contents of the file if it already exists.
        
        @type cb: function
        @param cb: (optional) a callback function that will be called to report
             progress on the download.  The callback should accept two integer
             parameters, the first representing the number of bytes that have
             been successfully transmitted from S3 and the second representing
             the total number of bytes that need to be transmitted.        
                    
        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.  
             
        @type policy: L{CannedACLString<boto.s3.acl.CannedACLStrings>}
        @param policy: A canned ACL policy that will be applied to the new key in S3.
             
        """
        fp = open(filename, 'rb')
        self.set_contents_from_file(fp, headers, replace, cb, num_cb, policy)
        fp.close()

    def set_contents_from_string(self, s, headers=None, replace=True, cb=None, num_cb=10, policy=None):
        """
        Store an object in S3 using the name of the Key object as the
        key in S3 and the string 's' as the contents.
        See set_contents_from_file method for details about the
        parameters.
        
        @type headers: dict
        @param headers: Additional headers to pass along with the request to AWS.
        
        @type replace: bool
        @param replace: If True, replaces the contents of the file if it already exists.
        
        @type cb: function
        @param cb: (optional) a callback function that will be called to report
             progress on the download.  The callback should accept two integer
             parameters, the first representing the number of bytes that have
             been successfully transmitted from S3 and the second representing
             the total number of bytes that need to be transmitted.        
                    
        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.  
             
        @type policy: L{CannedACLString<boto.s3.acl.CannedACLStrings>}
        @param policy: A canned ACL policy that will be applied to the new key in S3.
             
        """
        fp = StringIO.StringIO(s)
        self.set_contents_from_file(fp, headers, replace, cb, num_cb, policy)
        fp.close()

    def get_file(self, fp, headers=None, cb=None, num_cb=10, torrent=False):
        """
        Retrieves a file from an S3 Key
        
        @type fp: file
        @param fp: File pointer to put the data into
        
        @type headers: string
        @param: headers to send when retrieving the files
        
        @type cb: function
        @param cb: (optional) a callback function that will be called to report
             progress on the download.  The callback should accept two integer
             parameters, the first representing the number of bytes that have
             been successfully transmitted from S3 and the second representing
             the total number of bytes that need to be transmitted.
        
                    
        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.  
             
        @type torrent: bool
        @param torrent: Flag for whether to get a torrent for the file
        """
        if cb:
            if num_cb > 2:
                cb_count = self.size / self.BufferSize / (num_cb-2)
            else:
                cb_count = 0
            i = total_bytes = 0
            cb(total_bytes, self.size)
        save_debug = self.bucket.connection.debug
        if self.bucket.connection.debug == 1:
            self.bucket.connection.debug = 0
        
        if torrent: torrent = "torrent"
        self.open('r', headers, query_args=torrent)
        for bytes in self:
            fp.write(bytes)
            if cb:
                total_bytes += len(bytes)
                i += 1
                if i == cb_count:
                    cb(total_bytes, self.size)
                    i = 0
        if cb:
            cb(total_bytes, self.size)
        self.close()
        self.bucket.connection.debug = save_debug

    def get_torrent_file(self, fp, headers=None, cb=None, num_cb=10):
        """
        Get a torrent file (see to get_file)
        
        @type fp: file
        @param fp: The file pointer of where to put the torrent
        
        @type headers: dict
        @param headers: Headers to be passed
        
        @type cb: function
        @param cb: Callback function to call on retrieved data
        
        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.  
             
        """
        return self.get_file(fp, headers, cb, num_cb, torrent=True)
    
    def get_contents_to_file(self, fp, headers=None, cb=None, num_cb=10, torrent=False):
        """
        Retrieve an object from S3 using the name of the Key object as the
        key in S3.  Write the contents of the object to the file pointed
        to by 'fp'.
        
        @type fp: File -like object
        @param fp:
        
        @type headers: dict
        @param headers: additional HTTP headers that will be sent with the GET request.
        
        @type cb: function
        @param cb: (optional) a callback function that will be called to report
             progress on the download.  The callback should accept two integer
             parameters, the first representing the number of bytes that have
             been successfully transmitted from S3 and the second representing
             the total number of bytes that need to be transmitted.
             
                    
        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.  
             
        @type torrent: bool
        @param torrent: If True, returns the contents of a torrent file as a string.

        """
        if self.bucket != None:
            self.get_file(fp, headers, cb, num_cb, torrent=torrent)

    def get_contents_to_filename(self, filename, headers=None, cb=None, num_cb=10, torrent=False):
        """
        Retrieve an object from S3 using the name of the Key object as the
        key in S3.  Store contents of the object to a file named by 'filename'.
        See get_contents_to_file method for details about the
        parameters.
        
        @type filename: string
        @param filename: The filename of where to put the file contents
        
        @type headers: dict
        @param headers: Any additional headers to send in the request
        
        @type cb: function
        @param cb: (optional) a callback function that will be called to report
             progress on the download.  The callback should accept two integer
             parameters, the first representing the number of bytes that have
             been successfully transmitted from S3 and the second representing
             the total number of bytes that need to be transmitted.
             
                    
        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.  
             
        @type torrent: bool
        @param torrent: If True, returns the contents of a torrent file as a string.
        
        """
        fp = open(filename, 'wb')
        self.get_contents_to_file(fp, headers, cb, num_cb, torrent=torrent)
        fp.close()
        # if last_modified date was sent from s3, try to set file's timestamp
        if self.last_modified != None:
            try:
                modified_tuple = rfc822.parsedate_tz(self.last_modified)
                modified_stamp = int(rfc822.mktime_tz(modified_tuple))
                os.utime(fp.name, (modified_stamp, modified_stamp))
            except Exception, e: pass

    def get_contents_as_string(self, headers=None, cb=None, num_cb=10, torrent=False):
        """
        Retrieve an object from S3 using the name of the Key object as the
        key in S3.  Return the contents of the object as a string.
        See get_contents_to_file method for details about the
        parameters.
        
        @type headers: dict
        @param headers: Any additional headers to send in the request
        
        @type cb: function
        @param cb: (optional) a callback function that will be called to report
             progress on the download.  The callback should accept two integer
             parameters, the first representing the number of bytes that have
             been successfully transmitted from S3 and the second representing
             the total number of bytes that need to be transmitted.

        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.  
             
                    
        @type cb: int
        @param num_cb: (optional) If a callback is specified with the cb parameter
             this parameter determines the granularity of the callback by defining
             the maximum number of times the callback will be called during the file transfer.  
             
        @type torrent: bool
        @param torrent: If True, returns the contents of a torrent file as a string.
        
        @rtype: string
        @returns: The contents of the file as a string
        """
        fp = StringIO.StringIO()
        self.get_contents_to_file(fp, headers, cb, num_cb, torrent=torrent)
        return fp.getvalue()

    def add_email_grant(self, permission, email_address):
        """
        Convenience method that provides a quick way to add an email grant to a key.
        This method retrieves the current ACL, creates a new grant based on the parameters
        passed in, adds that grant to the ACL and then PUT's the new ACL back to S3.
        
        @type permission: string
        @param permission: The permission being granted.  Should be one of:
                            READ|WRITE|READ_ACP|WRITE_ACP|FULL_CONTROL
                            See http://docs.amazonwebservices.com/AmazonS3/2006-03-01/UsingAuthAccess.html
                            for more details on permissions.
        
        @type email_address: string
        @param email_address: The email address associated with the AWS account your are granting
                                the permission to.
        """
        policy = self.get_acl()
        policy.acl.add_email_grant(permission, email_address)
        self.set_acl(policy)

    def add_user_grant(self, permission, user_id):
        """
        Convenience method that provides a quick way to add a canonical user grant to a key.
        This method retrieves the current ACL, creates a new grant based on the parameters
        passed in, adds that grant to the ACL and then PUT's the new ACL back to S3.
        
        @type permission: string
        @param permission: The permission being granted.  Should be one of:
                            READ|WRITE|READ_ACP|WRITE_ACP|FULL_CONTROL
                            See http://docs.amazonwebservices.com/AmazonS3/2006-03-01/UsingAuthAccess.html
                            for more details on permissions.
        
        @type user_id: string
        @param user_id: The canonical user id associated with the AWS account your are granting
                        the permission to.
        """
        policy = self.get_acl()
        policy.acl.add_user_grant(permission, user_id)
        self.set_acl(policy)
