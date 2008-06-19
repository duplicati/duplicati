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
Represents an SQS Message
"""

import base64
import StringIO

class RawMessage:
    """
    Base class for SQS messages.  RawMessage does not encode the message
    in any way.  Whatever you store in the body of the message is what
    will be written to SQS and whatever is returned from SQS is stored
    directly into the body of the message.
    """
    
    def __init__(self, queue=None, body=''):
        self.queue = queue
        self._body = ''
        self.set_body(body)
        self.id = None

    def __len__(self):
        return len(self._body)

    def startElement(self, name, attrs, connection):
        return None

    def endElement(self, name, value, connection):
        if name == 'MessageBody':
            self.set_body(value)
        elif name == 'MessageId':
            self.id = value
        else:
            setattr(self, name, value)

    def set_body(self, body):
        """
        Set the body of the message.  You should always call this method
        rather than setting the attribute directly.
        """
        self._body = body

    def get_body(self):
        """
        Retrieve the body of the message.
        """
        return self._body
    
    def get_body_encoded(self):
        """
        This method is really a semi-private method used by the Queue.write
        method when writing the contents of the message to SQS.  The
        RawMessage class does not encode the message in any way so this
        just calls get_body().  You probably shouldn't need to call this
        method in the normal course of events.
        """
        return self.get_body()
    
    def change_visibility(self, vtimeout):
        """
        Convenience function to allow you to directly change the
        invisibility timeout for an individual message that has been
        read from an SQS queue.  This won't affect the default visibility
        timeout of the queue.
        """
        return self.queue.connection.change_message_visibility(self.queue.id,
                                                               self.id,
                                                               vtimeout)
class Message(RawMessage):
    """
    The default Message class used for SQS queues.  This class automatically
    encodes/decodes the message body using Base64 encoding to avoid any
    illegal characters in the message body.  See:

    http://developer.amazonwebservices.com/connect/thread.jspa?messageID=49680%EC%88%90

    for details on why this is a good idea.  The encode/decode is meant to
    be transparent to the end-user.
    """
    
    def endElement(self, name, value, connection):
        if name == 'MessageBody':
            # Decode the message body returned from SQS using base64
            self.set_body(base64.b64decode(value))
        elif name == 'MessageId':
            self.id = value
        else:
            setattr(self, name, value)

    def get_body_encoded(self):
        """
        Because the Message class encodes the message body in base64
        this private method used by queue.write needs to perform the
        encoding.
        """
        return base64.b64encode(self.get_body())

class MHMessage(Message):
    """
    The MHMessage class provides a message that provides RFC821-like
    headers like this:

    HeaderName: HeaderValue

    The encoding/decoding of this is handled automatically and after
    the message body has been read, the message instance can be treated
    like a mapping object, i.e. m['HeaderName'] would return 'HeaderValue'.
    """

    def __init__(self, queue=None, body='', xml_attrs=None):
        self._dict = {}
        Message.__init__(self, queue, body)

    def set_body(self, body):
        fp = StringIO.StringIO(body)
        line = fp.readline()
        while line:
            delim = line.find(':')
            key = line[0:delim]
            value = line[delim+1:].strip()
            self._dict[key.strip()] = value.strip()
            line = fp.readline()

    def get_body(self):
        s = ''
        for key,value in self._dict.items():
            s = s + '%s: %s\n' % (key, value)
        return s

    def __len__(self):
        return len(self.get_body())

    def __getitem__(self, key):
        if self._dict.has_key(key):
            return self._dict[key]
        else:
            raise KeyError(key)

    def __setitem__(self, key, value):
        self._dict[key] = value

    def keys(self):
        return self._dict.keys()

    def values(self):
        return self._dict.values()

    def items(self):
        return self._dict.items()

    def has_key(self, key):
        return self._dict.has_key(key)

    def update(self, d):
        return self._dict.update(d)

    def get(self, key, default=None):
        return self._dict.get(key, default)
        
