# Copyright (c) 2006,2007 Jon Colverson
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
This module was contributed by Jon Colverson.  It provides a couple of helper
functions that allow you to use M2Crypto's implementation of HTTPSConnection
rather than the default version in httplib.py.  The main benefit is that
M2Crypto's version verifies the certificate of the server.

To use this feature, do something like this:

from boto.ec2.connection import EC2Connection

ec2 = EC2Connection(ACCESS_KEY_ID, SECRET_ACCESS_KEY,
    https_connection_factory=https_connection_factory(cafile=CA_FILE))

See http://code.google.com/p/boto/issues/detail?id=57 for more details.
"""
from M2Crypto import SSL
from M2Crypto.httpslib import HTTPSConnection

def secure_context(cafile=None, capath=None):
    ctx = SSL.Context()
    ctx.set_verify(SSL.verify_peer | SSL.verify_fail_if_no_peer_cert, depth=9)
    if ctx.load_verify_locations(cafile=cafile, capath=capath) != 1:
        raise Exception("Couldn't load certificates")
    return ctx

def https_connection_factory(cafile=None, capath=None):
    def factory(*args, **kwargs):
        return HTTPSConnection(
            ssl_context=secure_context(cafile=cafile, capath=capath),
                *args, **kwargs)
    return (factory, (SSL.SSLError,))
