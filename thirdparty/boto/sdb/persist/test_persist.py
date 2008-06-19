from boto.sdb.persist.object import SDBObject
from boto.sdb.persist.property import *
from boto.sdb.persist import Manager
from datetime import datetime
import time

#
# This will eventually be moved to the boto.tests module and become a real unit test
# but for now it will live here.  It shows examples of each of the Property types in
# use and tests the basic operations.
#
class TestScalar(SDBObject):

    name = StringProperty()
    description = StringProperty()
    size = PositiveIntegerProperty()
    offset = IntegerProperty()
    foo = BooleanProperty()
    date = DateTimeProperty()
    file = S3KeyProperty()

class TestRef(SDBObject):

    name = StringProperty()
    ref = ObjectProperty(ref_class=TestScalar)

class TestSubClass1(TestRef):

    answer = PositiveIntegerProperty()

class TestSubClass2(TestScalar):

    flag = BooleanProperty()

class TestList(SDBObject):

    names = StringListProperty()
    numbers = PositiveIntegerListProperty()
    bools = BooleanListProperty()
    objects = ObjectListProperty(ref_class=TestScalar)
    
def test1():
    s = TestScalar()
    s.name = 'foo'
    s.description = 'This is foo'
    s.size = 42
    s.offset = -100
    s.foo = True
    s.date = datetime.now()
    s.save()
    return s

def test2(ref_name):
    s = TestRef()
    s.name = 'testref'
    rs = TestScalar.find(name=ref_name)
    s.ref = rs.next()
    s.save()
    return s

def test3():
    s = TestScalar()
    s.name = 'bar'
    s.description = 'This is bar'
    s.size = 24
    s.foo = False
    s.date = datetime.now()
    s.save()
    return s

def test4(ref1, ref2):
    s = TestList()
    s.names.append(ref1.name)
    s.names.append(ref2.name)
    s.numbers.append(ref1.size)
    s.numbers.append(ref2.size)
    s.bools.append(ref1.foo)
    s.bools.append(ref2.foo)
    s.objects.append(ref1)
    s.objects.append(ref2)
    s.save()
    return s

def test5(ref):
    s = TestSubClass1()
    s.answer = 42
    s.ref = ref
    s.save()
    # test out free form attribute
    s.fiddlefaddle = 'this is fiddlefaddle'
    s._fiddlefaddle = 'this is not fiddlefaddle'
    return s

def test6():
    s = TestSubClass2()
    s.name = 'fie'
    s.description = 'This is fie'
    s.size = 4200
    s.offset = -820
    s.foo = False
    s.date = datetime.now()
    s.flag = True
    s.save()
    return s

def test(domain_name):
    print 'Initialize the Persistance system'
    Manager.DefaultDomainName = domain_name
    print 'Call test1'
    s1 = test1()
    # now create a new instance and read the saved data from SDB
    print 'Now sleep to wait for things to converge'
    time.sleep(5)
    print 'Now lookup the object and compare the fields'
    s2 = TestScalar(s1.id)
    assert s1.name == s2.name
    assert s1.description == s2.description
    assert s1.size == s2.size
    assert s1.offset == s2.offset
    assert s1.foo == s2.foo
    #assert s1.date == s2.date
    print 'Call test2'
    s2 = test2(s1.name)
    print 'Call test3'
    s3 = test3()
    print 'Call test4'
    s4 = test4(s1, s3)
    print 'Call test5'
    s6 = test6()
    s5 = test5(s6)
    domain = s5._manager.domain
    item1 = domain.get_item(s1.id)
    item2 = domain.get_item(s2.id)
    item3 = domain.get_item(s3.id)
    item4 = domain.get_item(s4.id)
    item5 = domain.get_item(s5.id)
    item6 = domain.get_item(s6.id)
    return [(s1, item1), (s2, item2), (s3, item3), (s4, item4), (s5, item5), (s6, item6)]
