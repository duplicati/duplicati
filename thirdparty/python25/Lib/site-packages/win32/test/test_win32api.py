# General test module for win32api - please add some :)

import unittest

import win32api, win32con, win32event, winerror
import sys, os

class CurrentUserTestCase(unittest.TestCase):
    def testGetCurrentUser(self):
        name = "%s\\%s" % (win32api.GetDomainName(), win32api.GetUserName())
        self.failUnless(name == win32api.GetUserNameEx(win32api.NameSamCompatible))

class TestTime(unittest.TestCase):
    def testTimezone(self):
        # GetTimeZoneInformation
        rc, tzinfo = win32api.GetTimeZoneInformation()
        if rc == win32con.TIME_ZONE_ID_DAYLIGHT:
            tz_str = tzinfo[4]
            tz_time = tzinfo[5]
        else:
            tz_str = tzinfo[1]
            tz_time = tzinfo[2]
        # for the sake of code exercise but don't output
        tz_str.encode()
        tz_time.Format()
    def TestDateFormat(self):
        DATE_LONGDATE = 2
        date_flags = DATE_LONGDATE
        win32api.GetDateFormat(0, date_flags, None)
        win32api.GetDateFormat(0, date_flags, 0)
        win32api.GetDateFormat(0, date_flags, datetime.datetime.now())
        win32api.GetDateFormat(0, date_flags, time.time())
    def TestTimeFormat(self):
        win32api.GetTimeFormat(0, 0, None)
        win32api.GetTimeFormat(0, 0, 0)
        win32api.GetTimeFormat(0, 0, datetime.datetime.now())
        win32api.GetTimeFormat(0, 0, time.time())


class Registry(unittest.TestCase):
    key_name = r'PythonTestHarness\Whatever'
    def test1(self):
        # This used to leave a stale exception behind.
        def reg_operation():
            hkey = win32api.RegCreateKey(win32con.HKEY_CURRENT_USER, self.key_name)
            x = 3/0 # or a statement like: raise 'error'
        # do the test
        try:
            try:
                try:
                    reg_operation()
                except:
                    1/0 # Force exception
            finally:
                win32api.RegDeleteKey(win32con.HKEY_CURRENT_USER, self.key_name)
        except ZeroDivisionError:
            pass
    def testNotifyChange(self):
        def change():
            hkey = win32api.RegCreateKey(win32con.HKEY_CURRENT_USER, self.key_name)
            try:
                win32api.RegSetValue(hkey, None, win32con.REG_SZ, "foo")
            finally:
                win32api.RegDeleteKey(win32con.HKEY_CURRENT_USER, self.key_name)

        evt = win32event.CreateEvent(None,0,0,None)
        ## REG_NOTIFY_CHANGE_LAST_SET - values
        ## REG_CHANGE_NOTIFY_NAME - keys
        ## REG_NOTIFY_CHANGE_SECURITY - security descriptor
        ## REG_NOTIFY_CHANGE_ATTRIBUTES
        win32api.RegNotifyChangeKeyValue(win32con.HKEY_CURRENT_USER,1,win32api.REG_NOTIFY_CHANGE_LAST_SET,evt,True)
        ret_code=win32event.WaitForSingleObject(evt,0)
        # Should be no change.
        self.failUnless(ret_code==win32con.WAIT_TIMEOUT)
        change()
        # Our event should now be in a signalled state.
        ret_code=win32event.WaitForSingleObject(evt,0)
        self.failUnless(ret_code==win32con.WAIT_OBJECT_0)

class FileNames(unittest.TestCase):
    def testShortLongPathNames(self):
        try:
            me = __file__
        except NameError:
            me = sys.argv[0]
        fname = os.path.abspath(me)
        short_name = win32api.GetShortPathName(fname)
        long_name = win32api.GetLongPathName(short_name)
        self.failUnless(long_name==fname, \
                        "Expected long name ('%s') to be original name ('%s')" % (long_name, fname))
        self.failUnlessEqual(long_name, win32api.GetLongPathNameW(short_name))
        long_name = win32api.GetLongPathNameW(short_name)
        self.failUnless(type(long_name)==unicode, "GetLongPathNameW returned type '%s'" % (type(long_name),))
        self.failUnless(long_name==fname, \
                        "Expected long name ('%s') to be original name ('%s')" % (long_name, fname))

    def testShortUnicodeNames(self):
        try:
            me = __file__
        except NameError:
            me = sys.argv[0]
        fname = os.path.abspath(me)
        # passing unicode should cause GetShortPathNameW to be called.
        short_name = win32api.GetShortPathName(unicode(fname))
        self.failUnless(isinstance(short_name, unicode))
        long_name = win32api.GetLongPathName(short_name)
        self.failUnless(long_name==fname, \
                        "Expected long name ('%s') to be original name ('%s')" % (long_name, fname))
        self.failUnlessEqual(long_name, win32api.GetLongPathNameW(short_name))
        long_name = win32api.GetLongPathNameW(short_name)
        self.failUnless(type(long_name)==unicode, "GetLongPathNameW returned type '%s'" % (type(long_name),))
        self.failUnless(long_name==fname, \
                        "Expected long name ('%s') to be original name ('%s')" % (long_name, fname))

    def testLongLongPathNames(self):
        # We need filename where the FQN is > 256 - simplest way is to create a
        # 250 character directory in the cwd.
        import win32file
        basename = "a" * 250
        fname = "\\\\?\\" + os.path.join(os.getcwd(), basename)
        try:
            win32file.CreateDirectoryW(fname, None)
        except win32api.error, details:
            if details[0]!=winerror.ERROR_ALREADY_EXISTS:
                raise
        try:
            # GetFileAttributes automatically calls GetFileAttributesW when
            # passed unicode
            try:
                attr = win32api.GetFileAttributes(fname)
            except win32api.error, details:
                if details[0] != winerror.ERROR_FILENAME_EXCED_RANGE:
                    raise
        
            attr = win32api.GetFileAttributes(unicode(fname))
            self.failUnless(attr & win32con.FILE_ATTRIBUTE_DIRECTORY, attr)

            long_name = win32api.GetLongPathNameW(fname)
            self.failUnlessEqual(long_name, fname)
        finally:
            win32file.RemoveDirectory(fname)

class FormatMessage(unittest.TestCase):
    def test_FromString(self):
        msg = "Hello %1, how are you %2?"
        inserts = ["Mark", "today"]
        result = win32api.FormatMessage(win32con.FORMAT_MESSAGE_FROM_STRING,
                               msg, # source
                               0, # ID
                               0, # LangID
                               inserts)
        self.assertEqual(result, "Hello Mark, how are you today?")

class Misc(unittest.TestCase):
    def test_last_error(self):
        for x in (0, 1, -1, winerror.TRUST_E_PROVIDER_UNKNOWN):
            win32api.SetLastError(x)
            self.failUnlessEqual(x, win32api.GetLastError())

if __name__ == '__main__':
    unittest.main()
