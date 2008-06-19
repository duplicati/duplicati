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

from boto.exception import SDBPersistenceError
from boto.sdb.persist.checker import *
from boto.utils import Password

class Property(object):

    def __init__(self, checker_class, **params):
        self.name = ''
        self.checker = checker_class(**params)
        self.slot_name = '__'
        
    def set_name(self, name):
        self.name = name
        self.slot_name = '__' + self.name

class ScalarProperty(Property):

    def save(self, obj):
        domain = obj._manager.domain
        domain.put_attributes(obj.id, {self.name : self.to_string(obj)}, replace=True)

    def to_string(self, obj):
        return self.checker.to_string(getattr(obj, self.name))

    def load(self, obj):
        domain = obj._manager.domain
        a = domain.get_attributes(obj.id, self.name)
        # try to get the attribute value from SDB
        if self.name in a:
            value = self.checker.from_string(a[self.name], obj)
            setattr(obj, self.slot_name, value)
        # if it's not there, set the value to the default value
        else:
            self.__set__(obj, self.checker.default)

    def __get__(self, obj, objtype):
        if obj:
            try:
                value = getattr(obj, self.slot_name)
            except AttributeError:
                if obj._auto_update:
                    self.load(obj)
                    value = getattr(obj, self.slot_name)
                else:
                    value = self.checker.default
                    setattr(obj, self.slot_name, self.checker.default)
        return value

    def __set__(self, obj, value):
        self.checker.check(value)
        try:
            old_value = getattr(obj, self.slot_name)
        except:
            old_value = self.checker.default
        setattr(obj, self.slot_name, value)
        if obj._auto_update:
            try:
                self.save(obj)
            except:
                setattr(obj, self.slot_name, old_value)
                raise
                                      
class StringProperty(ScalarProperty):

    def __init__(self, **params):
        ScalarProperty.__init__(self, StringChecker, **params)

class PasswordProperty(ScalarProperty):
    """
    Hashed password
    """

    def __init__(self, **params):
        ScalarProperty.__init__(self, PasswordChecker, **params)

    def __set__(self, obj, value):
        p = Password()
        p.set(value)
        ScalarProperty.__set__(self, obj, p)

    def __get__(self, obj, objtype):
        return Password(ScalarProperty.__get__(self, obj, objtype))

class SmallPositiveIntegerProperty(ScalarProperty):

    def __init__(self, **params):
        params['size'] = 'small'
        params['signed'] = False
        ScalarProperty.__init__(self, IntegerChecker, **params)

class SmallIntegerProperty(ScalarProperty):

    def __init__(self, **params):
        params['size'] = 'small'
        params['signed'] = True
        ScalarProperty.__init__(self, IntegerChecker, **params)

class PositiveIntegerProperty(ScalarProperty):

    def __init__(self, **params):
        params['size'] = 'medium'
        params['signed'] = False
        ScalarProperty.__init__(self, IntegerChecker, **params)

class IntegerProperty(ScalarProperty):

    def __init__(self, **params):
        params['size'] = 'medium'
        params['signed'] = True
        ScalarProperty.__init__(self, IntegerChecker, **params)

class LargePositiveIntegerProperty(ScalarProperty):

    def __init__(self, **params):
        params['size'] = 'large'
        params['signed'] = False
        ScalarProperty.__init__(self, IntegerChecker, **params)

class LargeIntegerProperty(ScalarProperty):

    def __init__(self, **params):
        params['size'] = 'large'
        params['signed'] = True
        ScalarProperty.__init__(self, IntegerChecker, **params)

class BooleanProperty(ScalarProperty):

    def __init__(self, **params):
        ScalarProperty.__init__(self, BooleanChecker, **params)

class DateTimeProperty(ScalarProperty):

    def __init__(self, **params):
        ScalarProperty.__init__(self, DateTimeChecker, **params)

class ObjectProperty(ScalarProperty):

    def __init__(self, **params):
        ScalarProperty.__init__(self, ObjectChecker, **params)

class S3KeyProperty(ScalarProperty):

    def __init__(self, **params):
        ScalarProperty.__init__(self, S3KeyChecker, **params)
        
    def __set__(self, obj, value):
        self.checker.check(value)
        try:
            old_value = getattr(obj, self.slot_name)
        except:
            old_value = self.checker.default
        if isinstance(value, str):
            value = self.checker.from_string(value, obj)
        setattr(obj, self.slot_name, value)
        if obj._auto_update:
            try:
                self.save(obj)
            except:
                setattr(obj, self.slot_name, old_value)
                raise
                                      
class S3BucketProperty(ScalarProperty):

    def __init__(self, **params):
        ScalarProperty.__init__(self, S3BucketChecker, **params)
        
    def __set__(self, obj, value):
        self.checker.check(value)
        try:
            old_value = getattr(obj, self.slot_name)
        except:
            old_value = self.checker.default
        if isinstance(value, str):
            value = self.checker.from_string(value, obj)
        setattr(obj, self.slot_name, value)
        if obj._auto_update:
            try:
                self.save(obj)
            except:
                setattr(obj, self.slot_name, old_value)
                raise

class MultiValueProperty(Property):

    def __init__(self, checker_class, **params):
        Property.__init__(self, checker_class, **params)

    def __get__(self, obj, objtype):
        if obj:
            try:
                value = getattr(obj, self.slot_name)
            except AttributeError:
                if obj._auto_update:
                    self.load(obj)
                    value = getattr(obj, self.slot_name)
                else:
                    value = MultiValue(self, obj, [])
                    setattr(obj, self.slot_name, value)
        return value

    def load(self, obj):
        if obj != None:
            _list = []
            domain = obj._manager.domain
            a = domain.get_attributes(obj.id, self.name)
            if self.name in a:
                lst = a[self.name]
                if not isinstance(lst, list):
                    lst = [lst]
                for value in lst:
                    value = self.checker.from_string(value, obj)
                    _list.append(value)
        setattr(obj, self.slot_name, MultiValue(self, obj, _list))

    def __set__(self, obj, value):
        if not isinstance(value, list):
            raise SDBPersistenceError('Value must be a list')
        self._list = value
        str_list = []
        for value in self._list:
            str_list.append(self.checker.to_string(value))
        domain = obj._manager.domain
        try:
            domain.put_attributes(obj.id, {self.name : str_list}, replace=True)
        except:
            print 'problem setting value: %s' % value

class StringListProperty(MultiValueProperty):

    def __init__(self, **params):
        MultiValueProperty.__init__(self, StringChecker, **params)

class SmallIntegerListProperty(MultiValueProperty):

    def __init__(self, **params):
        params['size'] = 'small'
        params['signed'] = True
        MultiValueProperty.__init__(self, IntegerChecker, **params)

class SmallPositiveIntegerListProperty(MultiValueProperty):

    def __init__(self, **params):
        params['size'] = 'small'
        params['signed'] = False
        MultiValueProperty.__init__(self, IntegerChecker, **params)

class IntegerListProperty(MultiValueProperty):

    def __init__(self, **params):
        params['size'] = 'medium'
        params['signed'] = True
        MultiValueProperty.__init__(self, IntegerChecker, **params)

class PositiveIntegerListProperty(MultiValueProperty):

    def __init__(self, **params):
        params['size'] = 'medium'
        params['signed'] = False
        MultiValueProperty.__init__(self, IntegerChecker, **params)

class LargeIntegerListProperty(MultiValueProperty):

    def __init__(self, **params):
        params['size'] = 'large'
        params['signed'] = True
        MultiValueProperty.__init__(self, IntegerChecker, **params)

class LargePositiveIntegerListProperty(MultiValueProperty):

    def __init__(self, **params):
        params['size'] = 'large'
        params['signed'] = False
        MultiValueProperty.__init__(self, IntegerChecker, **params)

class BooleanListProperty(MultiValueProperty):

    def __init__(self, **params):
        MultiValueProperty.__init__(self, BooleanChecker, **params)

class ObjectListProperty(MultiValueProperty):

    def __init__(self, **params):
        MultiValueProperty.__init__(self, ObjectChecker, **params)
        
class HasManyProperty(Property):

    def set_name(self, name):
        self.name = name
        self.slot_name = '__' + self.name

    def __get__(self, obj, objtype):
        return self


class MultiValue:
    """
    Special Multi Value for boto persistence layer to allow us to do 
    obj.list.append(foo)
    """
    def __init__(self, property, obj, _list):
        self.checker = property.checker
        self.name = property.name
        self.object = obj
        self._list = _list

    def __repr__(self):
        return repr(self._list)

    def __getitem__(self, key):
        return self._list.__getitem__(key)

    def __delitem__(self, key):
        item = self[key]
        self._list.__delitem__(key)
        domain = self.object._manager.domain
        domain.delete_attributes(self.object.id, {self.name: [self.checker.to_string(item)]})

    def __len__(self):
        return len(self._list)

    def append(self, value):
        self.checker.check(value)
        self._list.append(value)
        domain = self.object._manager.domain
        domain.put_attributes(self.object.id, {self.name: self.checker.to_string(value)}, replace=False)

    def index(self, value):
        for x in self._list:
            if x.id == value.id:
                return self._list.index(x)

    def remove(self, value):
        del(self[self.index(value)])
