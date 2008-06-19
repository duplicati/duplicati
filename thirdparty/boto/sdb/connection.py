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
import xml.sax
import threading
import boto
from boto import handler
from boto.connection import AWSQueryConnection
from boto.sdb.domain import Domain
from boto.sdb.item import Item
from boto.exception import SDBResponseError
from boto.resultset import ResultSet

class ItemThread(threading.Thread):
    
    def __init__(self, name, domain_name, item_names):
        threading.Thread.__init__(self, name=name)
        print 'starting %s with %d items' % (name, len(item_names))
        self.domain_name = domain_name
        self.conn = SDBConnection()
        self.item_names = item_names
        self.items = []
        
    def run(self):
        for item_name in self.item_names:
            item = self.conn.get_attributes(self.domain_name, item_name)
            self.items.append(item)

class SDBConnection(AWSQueryConnection):

    APIVersion = '2007-11-07'
    SignatureVersion = '1'
    ResponseError = SDBResponseError

    def __init__(self, aws_access_key_id=None, aws_secret_access_key=None,
                 is_secure=False, port=None, proxy=None, proxy_port=None,
                 proxy_user=None, proxy_pass=None, host='sdb.amazonaws.com', debug=0,
                 https_connection_factory=None):
        AWSQueryConnection.__init__(self, aws_access_key_id, aws_secret_access_key,
                                    is_secure, port, proxy, proxy_port, proxy_user, proxy_pass,
                                    host, debug, https_connection_factory)
        self.box_usage = 0.0

    def build_name_value_list(self, params, attributes, replace=False):
        keys = attributes.keys()
        keys.sort()
        i = 1
        for key in keys:
            value = attributes[key]
            if isinstance(value, list):
                for v in value:
                    params['Attribute.%d.Name'%i] = key
                    params['Attribute.%d.Value'%i] = v
                    i += 1
            else:
                params['Attribute.%d.Name'%i] = key
                params['Attribute.%d.Value'%i] = value
                if replace:
                    params['Attribute.%d.Replace'%i] = 'true'
            i += 1

    def build_name_list(self, params, attribute_names):
        i = 1
        attribute_names.sort()
        for name in attribute_names:
            params['Attribute.%d.Name'%i] = name
            i += 1

    def get_usage(self):
        return self.box_usage

    def print_usage(self):
        print 'Total Usage: %f compute seconds' % self.box_usage
        cost = self.box_usage * 0.14
        print 'Approximate Cost: $%f' % cost

    def get_domain(self, domain_name, validate=True):
        """
        Returns a Domain object for a given domain_name.
        If the validate parameter is True, the domain_name is validated
        by performing a query (returning a max of 1 item) against the domain.
        """
        domain = Domain(self, domain_name)
        if validate:
            self.query(domain, '', max_items=1)
        return domain

    def lookup(self, domain_name):
        try:
            domain = self.get_domain(domain_name)
        except:
            domain = None
        return domain

    def get_all_domains(self, max_domains=None, next_token=None):
        params = {}
        if max_domains:
            params['MaxNumberOfDomains'] = max_domains
        if next_token:
            params['NextToken'] = next_token
        return self.get_list('ListDomains', params, [('DomainName', Domain)])
        
    def create_domain(self, domain_name):
        params = {'DomainName':domain_name}
        d = self.get_object('CreateDomain', params, Domain)
        d.name = domain_name
        return d

    def get_domain_and_name(self, domain_or_name):
        if (isinstance(domain_or_name, Domain)):
            return (domain_or_name, domain_or_name.name)
        else:
            return (self.get_domain(domain_or_name), domain_or_name)
        
    def delete_domain(self, domain_or_name):
        domain, domain_name = self.get_domain_and_name(domain_or_name)
        params = {'DomainName':domain_name}
        return self.get_status('DeleteDomain', params)
        
    def put_attributes(self, domain_or_name, item_name, attributes, replace=True):
        """
        Store attributes for a given item in a domain.
        Parameters:
            domain__or_name - either a domain object or the name of a domain in SimpleDB
            item_name - the name of the SDB item the attributes will be
                        associated with
            attributes - a dict containing the name/value pairs to store
                         as attributes
            replace - a boolean value that determines whether the attribute
                      values passed in will replace any existing values or will
                      be added as additional values.  Defaults to True.
        Returns:
            Boolean True or raises an exception
        """
        domain, domain_name = self.get_domain_and_name(domain_or_name)
        params = {'DomainName' : domain_name,
                  'ItemName' : item_name}
        self.build_name_value_list(params, attributes, replace)
        return self.get_status('PutAttributes', params)

    def get_attributes(self, domain_or_name, item_name, attribute_name=None, item=None):
        domain, domain_name = self.get_domain_and_name(domain_or_name)
        params = {'DomainName' : domain_name,
                  'ItemName' : item_name}
        if attribute_name:
            params['AttributeName'] = attribute_name
        response = self.make_request('GetAttributes', params)
        body = response.read()
        if response.status == 200:
            if item == None:
                item = Item(domain, item_name)
            h = handler.XmlHandler(item, self)
            xml.sax.parseString(body, h)
            return item
        else:
            raise SDBResponseError(response.status, response.reason, body)
        
    def delete_attributes(self, domain_or_name, item_name, attr_names=None):
        """
        Delete attributes from a given item in a domain.
        Parameters:
            domain__or_name - either a domain object or the name of a domain in SimpleDB
            item_name - the name of the SDB item the attributes will be
                        removed from
            attributes - either a list containing attribute names which will cause
                         all values associated with that attribute name to be deleted or
                         a dict containing the attribute names and keys and list of values
                         to delete as the value
        Returns:
            Boolean True or raises an exception
        """
        domain, domain_name = self.get_domain_and_name(domain_or_name)
        params = {'DomainName':domain_name,
                  'ItemName' : item_name}
        if attr_names:
            if isinstance(attr_names, list):
                self.build_name_list(params, attr_names)
            elif isinstance(attr_names, dict):
                self.build_name_value_list(params, attr_names)
        return self.get_status('DeleteAttributes', params)
        
    def query(self, domain_or_name, query='', max_items=None, next_token=None):
        """
        Returns a list of item names within domain_name that match the query.
        """
        domain, domain_name = self.get_domain_and_name(domain_or_name)
        params = {'DomainName':domain_name,
                  'QueryExpression' : query}
        if max_items:
            params['MaxNumberOfItems'] = max_items
        if next_token:
            params['NextToken'] = next_token
        return self.get_object('Query', params, ResultSet)

    def threaded_query(self, domain_or_name, query='', max_items=None, next_token=None, num_threads=6):
        """
        Returns a list of fully populated items that match the query provided.

        The name/value pairs for all of the matching item names are retrieved in a number of separate
        threads (specified by num_threads) to achieve maximum throughput.
        The ResultSet that is returned has an attribute called next_token that can be used
        to retrieve additional results for the same query.
        """
        domain, domain_name = self.get_domain_and_name(domain_or_name)
        if max_items and num_threads > max_items:
            num_threads = max_items
        rs = self.query(domain_or_name, query, max_items, next_token)
        threads = []
        n = len(rs) / num_threads
        for i in range(0, num_threads):
            if i+1 == num_threads:
                thread = ItemThread('Thread-%d' % i, domain_name, rs[n*i:])
            else:
                thread = ItemThread('Thread-%d' % i, domain_name, rs[n*i:n*(i+1)])
            threads.append(thread)
            thread.start()
        del rs[0:]
        for thread in threads:
            thread.join()
            for item in thread.items:
                rs.append(item)
        return rs

