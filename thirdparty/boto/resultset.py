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

class ResultSet(list):
    """
    The ResultSet is used to pass results back from the Amazon services
    to the client.  It has an ugly but workable mechanism for parsing
    the XML results from AWS.  Because I don't really want any dependencies
    on external libraries, I'm using the standard SAX parser that comes
    with Python.  The good news is that it's quite fast and efficient but
    it makes some things rather difficult.

    You can pass in, as the marker_elem parameter, a list of tuples.
    Each tuple contains a string as the first element which represents
    the XML element that the resultset needs to be on the lookout for
    and a Python class as the second element of the tuple.  Each time the
    specified element is found in the XML, a new instance of the class
    will be created and popped onto the stack.

    """

    def __init__(self, marker_elem=None):
        list.__init__(self)
        if isinstance(marker_elem, list):
            self.markers = marker_elem
        else:
            self.markers = []
        self.index = 0
        self.marker = None
        self.is_truncated = False
        self.next_token = None
        self.status = True

    def __iter__(self):
        return self

    def next(self):
        if self.index == len(self):
            self.index = 0
            raise StopIteration
        self.index += 1
        return self[self.index-1]

    def startElement(self, name, attrs, connection):
        for t in self.markers:
            if name == t[0]:
                obj = t[1](connection)
                self.append(obj)
                return obj
        return None

    def to_boolean(self, value, true_value='true'):
        if value == true_value:
            return True
        else:
            return False

    def endElement(self, name, value, connection):
        if name == 'IsTruncated':
            self.is_truncated = self.to_boolean(value)
        elif name == 'Marker':
            self.marker = value
        elif name == 'Prefix':
            self.prefix = value
        elif name == 'return':
            self.status = self.to_boolean(value)
        elif name == 'StatusCode':
            self.status = self.to_boolean(value, 'Success')
        elif name == 'ItemName':
            self.append(value)
        elif name == 'NextToken':
            self.next_token = value
        elif name == 'BoxUsage':
            try:
                connection.box_usage += float(value)
            except:
                pass
        else:
            setattr(self, name, value)
        
