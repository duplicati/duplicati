# Copyright (c) 2006-2008 Mitch Garnaat http://garnaat.org/
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

import random, time, os, datetime
import boto
from boto.sdb.persist.object import SDBObject
from boto.sdb.persist.property import *

class Identifier(object):

    _hex_digits = '0123456789abcdef'

    @classmethod
    def gen(cls, prefix):
        suffix = ''
        for i in range(0,8):
            suffix += random.choice(cls._hex_digits)
        return ts + '-' + suffix
    
class Version(SDBObject):

    name = StringProperty()
    pdb = ObjectProperty(ref_class=SDBObject)
    date = DateTimeProperty()
    
    def __init__(self, id=None, manager=None):
        SDBObject.__init__(self, id, manager)
        if id == None:
            self.name = Identifier.gen('v')
            self.date = datetime.datetime.now()
            print 'created Version %s' % self.name

    def partitions(self):
        """
        Return an iterator containing all Partition objects related to this Version.

        @rtype: iterator of L{Partitions<boto.mapreduce.partitiondb.Partition>}
        @return: The Partitions in this Version
        """
        return self.get_related_objects('version', Partition)

    def add_partition(self, name=None):
        """
        Add a new Partition to this Version.

        @type name: string
        @param name: The name of the new Partition (optional)

        @rtype: L{Partition<boto.mapreduce.partitiondb.Partition>}
        @return: The new Partition object
        """
        p = Partition(manager=self.manager, name=name)
        p.version = self
        p.pdb = self.pdb
        p.save()
        return p

    def get_s3_prefix(self):
        if not self.pdb:
            raise ValueError, 'pdb attribute must be set to compute S3 prefix'
        return self.pdb.get_s3_prefix() + self.name + '/'

class PartitionDB(SDBObject):

    name = StringProperty()
    bucket_name = StringProperty()
    versions = ObjectListProperty(ref_class=Version)

    def __init__(self, id=None, manager=None, name='', bucket_name=''):
        SDBObject.__init__(self, id, manager)
        if id == None:
            self.name = name
            self.bucket_name = bucket_name

    def get_s3_prefix(self):
        return self.name + '/'

    def add_version(self):
        """
        Add a new Version to this PartitionDB.  The newly added version becomes the
        current version.

        @rtype: L{Version<boto.mapreduce.partitiondb.Version>}
        @return: The newly created Version object.
        """
        v = Version()
        v.pdb = self
        v.save()
        self.versions.append(v)
        return v

    def revert(self):
        """
        Revert to the previous version of this PartitionDB.  The current version is removed from the
        list of Versions and the Version immediately preceeding it becomes the current version.
        Note that this method does not delete the Version object or any Partitions related to the
        Version object.

        @rtype: L{Version<boto.mapreduce.partitiondb.Version>}
        @return: The previous current Version object.
        """
        v = self.current_version()
        if v:
            self.versions.remove(v)
        return v

    def current_version(self):
        """
        Get the currently active Version of this PartitionDB object.

        @rtype: L{Version<boto.mapreduce.partitiondb.Version>}
        @return: The current Version object or None if there are no Versions associated
                 with this PartitionDB object.
        """
        if self.versions:
            if len(self.versions) > 0:
                return self.versions[-1]
        return None

class Partition(SDBObject):

    def __init__(self, id=None, manager=None, name=None):
        SDBObject.__init__(self, id, manager)
        if id == None:
            self.name = name

    name = StringProperty()
    version = ObjectProperty(ref_class=Version)
    pdb = ObjectProperty(ref_class=PartitionDB)
    data = S3KeyProperty()

    def get_key_name(self):
        return self.version.get_s3_prefix() + self.name

    def upload(self, path, bucket_name=None):
        if not bucket_name:
            bucket_name = self.version.pdb.bucket_name
        s3 = self.manager.get_s3_connection()
        bucket = s3.lookup(bucket_name)
        directory, filename = os.path.split(path)
        self.name = filename
        key = bucket.new_key(self.get_key_name())
        key.set_contents_from_filename(path)
        self.data = key
        self.save()

    def delete(self):
        if self.data:
            self.data.delete()
        SDBObject.delete(self)
        
        
        
