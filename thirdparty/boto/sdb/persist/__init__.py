# Copyright (c) 2006,2007,2008 Mitch Garnaat http://garnaat.org/
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

import boto
from boto.utils import find_class

class Manager(object):

    DefaultDomainName = boto.config.get('Persist', 'default_domain', None)

    def __init__(self, domain_name=None, aws_access_key_id=None, aws_secret_access_key=None, debug=0):
        self.domain_name = domain_name
        self.aws_access_key_id = aws_access_key_id
        self.aws_secret_access_key = aws_secret_access_key
        self.domain = None
        self.sdb = None
        self.s3 = None
        if not self.domain_name:
            self.domain_name = self.DefaultDomainName
            if self.domain_name:
                boto.log.info('No SimpleDB domain set, using default_domain: %s' % self.domain_name)
        else:
            boto.log.warning('No SimpleDB domain set, persistance is disabled')
        if self.domain_name:
            self.sdb = boto.connect_sdb(aws_access_key_id=self.aws_access_key_id,
                                        aws_secret_access_key=self.aws_secret_access_key,
                                        debug=debug)
            self.domain = self.sdb.lookup(self.domain_name)
            if not self.domain:
                self.domain = self.sdb.create_domain(self.domain_name)

    def get_s3_connection(self):
        if not self.s3:
            self.s3 = boto.connect_s3(self.aws_access_key_id, self.aws_secret_access_key)
        return self.s3

def get_manager(domain_name=None, aws_access_key_id=None, aws_secret_access_key=None, debug=0):
    return Manager(domain_name, aws_access_key_id, aws_secret_access_key, debug=debug)

def set_domain(domain_name):
    Manager.DefaultDomainName = domain_name

def get_domain():
    return Manager.DefaultDomainName

def revive_object_from_id(id, manager):
    if not manager.domain:
        return None
    attrs = manager.domain.get_attributes(id, ['__module__', '__type__', '__lineage__'])
    try:
        cls = find_class(attrs['__module__'], attrs['__type__'])
        return cls(id, manager=manager)
    except ImportError:
        return None

def object_lister(cls, query_lister, manager):
    for item in query_lister:
        if cls:
            yield cls(item.name)
        else:
            o = revive_object_from_id(item.name, manager)
            if o:
                yield o
                

