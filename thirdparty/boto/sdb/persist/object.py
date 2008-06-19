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
from boto.sdb.persist import get_manager, object_lister
from boto.sdb.persist.property import *
import uuid

class SDBBase(type):
    "Metaclass for all SDBObjects"
    def __init__(cls, name, bases, dict):
        super(SDBBase, cls).__init__(name, bases, dict)
        # Make sure this is a subclass of SDBObject - mainly copied from django ModelBase (thanks!)
        try:
            if filter(lambda b: issubclass(b, SDBObject), bases):
                # look for all of the Properties and set their names
                for key in dict.keys():
                    if isinstance(dict[key], Property):
                        property = dict[key]
                        property.set_name(key)
                prop_names = []
                props = cls.properties()
                for prop in props:
                    prop_names.append(prop.name)
                setattr(cls, '_prop_names', prop_names)
        except NameError:
            # 'SDBObject' isn't defined yet, meaning we're looking at our own
            # SDBObject class, defined below.
            pass
        
class SDBObject(object):
    __metaclass__ = SDBBase

    _manager = get_manager()

    @classmethod
    def get_lineage(cls):
        l = [c.__name__ for c in cls.mro()]
        l.reverse()
        return '.'.join(l)
    
    @classmethod
    def get(cls, id=None, **params):
        if cls._manager.domain and id:
            a = cls._manager.domain.get_attributes(id, '__type__')
            if a.has_key('__type__'):
                return cls(id)
            else:
                raise SDBPersistenceError('%s object with id=%s does not exist' % (cls.__name__, id))
        else:
            rs = cls.find(**params)
            try:
                obj = rs.next()
            except StopIteration:
                raise SDBPersistenceError('%s object matching query does not exist' % cls.__name__)
            try:
                rs.next()
            except StopIteration:
                return obj
            raise SDBPersistenceError('Query matched more than 1 item')

    @classmethod
    def find(cls, **params):
        keys = params.keys()
        if len(keys) > 4:
            raise SDBPersistenceError('Too many fields, max is 4')
        parts = ["['__type__'='%s'] union ['__lineage__'starts-with'%s']" % (cls.__name__, cls.get_lineage())]
        properties = cls.properties()
        for key in keys:
            found = False
            for property in properties:
                if property.name == key:
                    found = True
                    if isinstance(property, ScalarProperty):
                        checker = property.checker
                        parts.append("['%s' = '%s']" % (key, checker.to_string(params[key])))
                    else:
                        raise SDBPersistenceError('%s is not a searchable field' % key)
            if not found:
                raise SDBPersistenceError('%s is not a valid field' % key)
        query = ' intersection '.join(parts)
        if cls._manager.domain:
            rs = cls._manager.domain.query(query)
        else:
            rs = []
        return object_lister(None, rs, cls._manager)

    @classmethod
    def list(cls, max_items=None):
        if cls._manager.domain:
            rs = cls._manager.domain.query("['__type__' = '%s']" % cls.__name__, max_items=max_items)
        else:
            rs = []
        return object_lister(cls, rs, cls._manager)

    @classmethod
    def properties(cls):
        properties = []
        while cls:
            for key in cls.__dict__.keys():
                if isinstance(cls.__dict__[key], ScalarProperty):
                    properties.append(cls.__dict__[key])
            if len(cls.__bases__) > 0:
                cls = cls.__bases__[0]
            else:
                cls = None
        return properties

    # for backwards compatibility
    find_properties = properties

    def __init__(self, id=None, manager=None):
        if manager:
            self._manager = manager
        self.id = id
        if self.id:
            self._auto_update = True
            if self._manager.domain:
                attrs = self._manager.domain.get_attributes(self.id, '__type__')
                if len(attrs.keys()) == 0:
                    raise SDBPersistenceError('Object %s: not found' % self.id)
        else:
            self.id = str(uuid.uuid4())
            self._auto_update = False

    def __setattr__(self, name, value):
        if name in self._prop_names:
            object.__setattr__(self, name, value)
        elif name.startswith('_'):
            object.__setattr__(self, name, value)
        elif name == 'id':
            object.__setattr__(self, name, value)
        else:
            self._persist_attribute(name, value)
            object.__setattr__(self, name, value)

    def __getattr__(self, name):
        if not name.startswith('_'):
            a = self._manager.domain.get_attributes(self.id, name)
            if a.has_key(name):
                object.__setattr__(self, name, a[name])
                return a[name]
        raise AttributeError

    def __repr__(self):
        return '%s<%s>' % (self.__class__.__name__, self.id)

    def _persist_attribute(self, name, value):
        if self.id:
            self._manager.domain.put_attributes(self.id, {name : value}, replace=True)

    def _get_sdb_item(self):
        return self._manager.domain.get_item(self.id)

    def save(self):
        attrs = {'__type__' : self.__class__.__name__,
                 '__module__' : self.__class__.__module__,
                 '__lineage__' : self.get_lineage()}
        for property in self.properties():
            attrs[property.name] = property.to_string(self)
        if self._manager.domain:
            self._manager.domain.put_attributes(self.id, attrs, replace=True)
            self._auto_update = True
        
    def delete(self):
        if self._manager.domain:
            self._manager.domain.delete_attributes(self.id)

    def get_related_objects(self, ref_name, ref_cls=None):
        if self._manager.domain:
            query = "['%s' = '%s']" % (ref_name, self.id)
            if ref_cls:
                query += " intersection ['__type__'='%s']" % ref_cls.__name__
            rs = self._manager.domain.query(query)
        else:
            rs = []
        return object_lister(ref_cls, rs, self._manager)

