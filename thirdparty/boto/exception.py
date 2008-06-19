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

"""
Exception classes - Subclassing allows you to check for specific errors
"""

class BotoClientError(Exception):
    """
    General Boto Client error (error accessing AWS)
    """
    
    def __init__(self, reason):
        self.reason = reason

    def __repr__(self):
        return 'S3Error: %s' % self.reason

    def __str__(self):
        return 'S3Error: %s' % self.reason

class SDBPersistenceError(Exception):

    pass

class S3PermissionsError(BotoClientError):
    """
    Permissions error when accessing a bucket or key on S3.
    """
    pass
    
class BotoServerError(Exception):
    
    def __init__(self, status, reason, body=''):
        self.status = status
        self.reason = reason
        self.body = body

    def __repr__(self):
        return '%s: %s %s\n%s' % (self.__class__.__name__,
                                  self.status, self.reason, self.body)

    def __str__(self):
        return '%s: %s %s\n%s' % (self.__class__.__name__,
                                  self.status, self.reason, self.body)

class S3CreateError(BotoServerError):
    """
    Error creating a bucket or key on S3.
    """
    pass

class S3CopyError(BotoServerError):
    """
    Error copying a key on S3.
    """
    pass

class SQSError(BotoServerError):
    """
    General Error on Simple Queue Service.
    """
    pass
    
class S3ResponseError(BotoServerError):
    """
    Error in response from S3.
    """
    pass

class EC2ResponseError(BotoServerError):
    """
    Error in response from EC2.
    """
    pass

class SDBResponseError(BotoServerError):
    """
    Error in respones from SDB.
    """
    pass

class AWSConnectionError(BotoClientError):
    """
    General error connecting to Amazon Web Services.
    """
    pass

class S3DataError(BotoClientError):
    """
    Error receiving data from S3.
    """ 
    pass

class FPSResponseError(BotoServerError):
    pass
