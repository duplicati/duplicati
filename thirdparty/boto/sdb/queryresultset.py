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

from boto.sdb.item import Item

def query_lister(domain, query='', max_items=None):
    more_results = True
    num_results = 0
    next_token = None
    while more_results:
        rs = domain.connection.query(domain.name, query, None, next_token)
        for item_name in rs:
            if max_items:
                if num_results == max_items:
                    raise StopIteration
            yield Item(domain, item_name)
            num_results += 1
        next_token = rs.next_token
        more_results = next_token != None
        
class QueryResultSet:

    def __init__(self, domain=None, query='', max_items=None):
        self.max_items = max_items
        self.domain = domain
        self.query = query

    def __iter__(self):
        return query_lister(self.domain, self.query, self.max_items)


    
