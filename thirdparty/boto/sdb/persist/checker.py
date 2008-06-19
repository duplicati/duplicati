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

from datetime import datetime
import boto
from boto.s3.key import Key
from boto.s3.bucket import Bucket
from boto.sdb.persist import revive_object_from_id
from boto.exception import SDBPersistenceError
from boto.utils import Password

ISO8601 = '%Y-%m-%dT%H:%M:%SZ'

class ValueChecker:

    def check(self, value):
        """
        Checks a value to see if it is of the right type.

        Should raise a TypeError exception if an in appropriate value is passed in.
        """
        raise TypeError

    def from_string(self, str_value, obj):
        """
        Takes a string as input and returns the type-specific value represented by that string.

        Should raise a ValueError if the value cannot be converted to the appropriate type.
        """
        raise ValueError

    def to_string(self, value):
        """
        Convert a value to it's string representation.

        Should raise a ValueError if the value cannot be converted to a string representation.
        """
        raise ValueError
    
class StringChecker(ValueChecker):

    def __init__(self, **params):
        if params.has_key('maxlength'):
            self.maxlength = params['maxlength']
        else:
            self.maxlength = 1024
        if params.has_key('default'):
            self.check(params['default'])
            self.default = params['default']
        else:
            self.default = ''

    def check(self, value):
        if isinstance(value, str) or isinstance(value, unicode):
            if len(value) > self.maxlength:
                raise ValueError, 'Length of value greater than maxlength'
        else:
            raise TypeError, 'Expecting String, got %s' % type(value)

    def from_string(self, str_value, obj):
        return str_value

    def to_string(self, value):
        self.check(value)
        return value

class PasswordChecker(StringChecker):
    def check(self, value):
        if isinstance(value, str) or isinstance(value, unicode) or isinstance(value, Password):
            if len(value) > self.maxlength:
                raise ValueError, 'Length of value greater than maxlength'
        else:
            raise TypeError, 'Expecting String, got %s' % type(value)

class IntegerChecker(ValueChecker):

    __sizes__ = { 'small' : (65535, 32767, -32768, 5),
                  'medium' : (4294967295, 2147483647, -2147483648, 10),
                  'Large' : (18446744073709551615, 9223372036854775807, -9223372036854775808, 20)}

    def __init__(self, **params):
        self.size = params.get('size', 'medium')
        if self.size not in self.__sizes__.keys():
            raise ValueError, 'size must be one of %s' % self.__siz8es__.keys()
        self.signed = params.get('signed', True)
        self.default = params.get('default', 0)
        self.format_string = '%%0%dd' % self.__sizes__[self.size][-1]

    def check(self, value):
        if not isinstance(value, int) and not isinstance(value, long):
            raise TypeError, 'Expecting int or long, got %s' % type(value)
        if self.signed:
            min = self.__sizes__[self.size][2]
            max = self.__sizes__[self.size][1]
        else:
            min = 0
            max = self.__sizes__[self.size][0]
        if value > max:
            raise ValueError, 'Maximum value is %d' % max
        if value < min:
            raise ValueError, 'Minimum value is %d' % min

    def from_string(self, str_value, obj):
        val = int(str_value)
        if self.signed:
            val = val + self.__sizes__[self.size][2]
        return val

    def to_string(self, value):
        self.check(value)
        if self.signed:
            value += -self.__sizes__[self.size][2]
        return self.format_string % value
    
class BooleanChecker(ValueChecker):

    def __init__(self, **params):
        if params.has_key('default'):
            self.default = params['default']
        else:
            self.default = False

    def check(self, value):
        if not isinstance(value, bool):
            raise TypeError, 'Expecting bool, got %s' % type(value)

    def from_string(self, str_value, obj):
        if str_value.lower() == 'true':
            return True
        else:
            return False
        
    def to_string(self, value):
        self.check(value)
        if value == True:
            return 'true'
        else:
            return 'false'
    
class DateTimeChecker(ValueChecker):

    def __init__(self, **params):
        if params.has_key('maxlength'):
            self.maxlength = params['maxlength']
        else:
            self.maxlength = 1024
        if params.has_key('default'):
            self.default = params['default']
        else:
            self.default = datetime.now()

    def check(self, value):
        if not isinstance(value, datetime):
            raise TypeError, 'Expecting datetime, got %s' % type(value)

    def from_string(self, str_value, obj):
        try:
            return datetime.strptime(str_value, ISO8601)
        except:
            raise ValueError, 'Unable to convert %s to DateTime' % str_value

    def to_string(self, value):
        self.check(value)
        return value.strftime(ISO8601)
    
class ObjectChecker(ValueChecker):

    def __init__(self, **params):
        self.default = None
        self.ref_class = params.get('ref_class', None)
        if self.ref_class == None:
            raise SDBPersistenceError('ref_class parameter is required')

    def check(self, value):
        if value == None:
            return
        if isinstance(value, str) or isinstance(value, unicode):
            # ugly little hack - sometimes I want to just stick a UUID string
            # in here rather than instantiate an object. 
            # This does a bit of hand waving to "type check" the string
            t = value.split('-')
            if len(t) != 5:
                raise ValueError
        else:
            try:
                obj_lineage = value.get_lineage()
                cls_lineage = self.ref_class.get_lineage()
                if obj_lineage.startswith(cls_lineage):
                    return
                raise TypeError, '%s not instance of %s' % (obj_lineage, cls_lineage)
            except:
                raise ValueError, '%s is not an SDBObject' % value

    def from_string(self, str_value, obj):
        if not str_value:
            return None
        try:
            return revive_object_from_id(str_value, obj._manager)
        except:
            raise ValueError, 'Unable to convert %s to Object' % str_value

    def to_string(self, value):
        self.check(value)
        if isinstance(value, str) or isinstance(value, unicode):
            return value
        if value == None:
            return ''
        else:
            return value.id

class S3KeyChecker(ValueChecker):

    def __init__(self, **params):
        self.default = None

    def check(self, value):
        if value == None:
            return
        if isinstance(value, str) or isinstance(value, unicode):
            try:
                bucket_name, key_name = value.split('/', 1)
            except:
                raise ValueError
        elif not isinstance(value, Key):
            raise TypeError, 'Expecting Key, got %s' % type(value)

    def from_string(self, str_value, obj):
        if not str_value:
            return None
        if str_value == 'None':
            return None
        try:
            bucket_name, key_name = str_value.split('/', 1)
            if obj:
                s3 = obj._manager.get_s3_connection()
                bucket = s3.get_bucket(bucket_name)
                key = bucket.get_key(key_name)
                if not key:
                    key = bucket.new_key(key_name)
                return key
        except:
            raise ValueError, 'Unable to convert %s to S3Key' % str_value

    def to_string(self, value):
        self.check(value)
        if isinstance(value, str) or isinstance(value, unicode):
            return value
        if value == None:
            return ''
        else:
            return '%s/%s' % (value.bucket.name, value.name)

class S3BucketChecker(ValueChecker):

    def __init__(self, **params):
        self.default = None

    def check(self, value):
        if value == None:
            return
        if isinstance(value, str) or isinstance(value, unicode):
            return
        elif not isinstance(value, Bucket):
            raise TypeError, 'Expecting Bucket, got %s' % type(value)

    def from_string(self, str_value, obj):
        if not str_value:
            return None
        if str_value == 'None':
            return None
        try:
            if obj:
                s3 = obj._manager.get_s3_connection()
                bucket = s3.get_bucket(str_value)
                return bucket
        except:
            raise ValueError, 'Unable to convert %s to S3Bucket' % str_value

    def to_string(self, value):
        self.check(value)
        if value == None:
            return ''
        else:
            return '%s' % value.name

