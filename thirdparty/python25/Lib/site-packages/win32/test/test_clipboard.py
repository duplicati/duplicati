# General test module for win32api - please add some :)
import sys, os
import unittest

from win32clipboard import *
import win32gui, win32con
import pywintypes
import array

custom_format_name = "PythonClipboardTestFormat"

class CrashingTestCase(unittest.TestCase):
    def test_722082(self):
        class crasher(object):
            pass

        obj = crasher()
        OpenClipboard()
        try:
            EmptyClipboard()
            # This used to crash - now correctly raises type error.
            self.assertRaises(TypeError, SetClipboardData, 0, obj )
        finally:
            CloseClipboard()

class TestBitmap(unittest.TestCase):
    def setUp(self):
        self.bmp_handle = None
        try:
            this_file = __file__
        except NameError:
            this_file = sys.argv[0]
        this_dir = os.path.dirname(__file__)
        self.bmp_name = os.path.join(os.path.abspath(this_dir),
                                     "..", "Demos", "images", "smiley.bmp")
        self.failUnless(os.path.isfile(self.bmp_name))
        flags = win32con.LR_DEFAULTSIZE | win32con.LR_LOADFROMFILE
        self.bmp_handle = win32gui.LoadImage(0, self.bmp_name,
                                             win32con.IMAGE_BITMAP,
                                             0, 0, flags)
        self.failUnless(self.bmp_handle, "Failed to get a bitmap handle")

    def tearDown(self):
        if self.bmp_handle:
            win32gui.DeleteObject(self.bmp_handle)

    def test_bitmap_roundtrip(self):
        OpenClipboard()
        try:
            SetClipboardData(win32con.CF_BITMAP, self.bmp_handle)
            got_handle = GetClipboardDataHandle(win32con.CF_BITMAP)
            self.failUnlessEqual(got_handle, self.bmp_handle)
        finally:
            CloseClipboard()

class TestStrings(unittest.TestCase):
    def setUp(self):
        OpenClipboard()
    def tearDown(self):
        CloseClipboard()
    def test_unicode(self):
        val = unicode("test-\xe0\xf2", "mbcs")
        SetClipboardData(win32con.CF_UNICODETEXT, val)
        self.failUnlessEqual(GetClipboardData(win32con.CF_UNICODETEXT), val)
    def test_string(self):
        val = "test"
        SetClipboardData(win32con.CF_TEXT, val)
        self.failUnlessEqual(GetClipboardData(win32con.CF_TEXT), val)

class TestGlobalMemory(unittest.TestCase):
    def setUp(self):
        OpenClipboard()
    def tearDown(self):
        CloseClipboard()
    def test_mem(self):
        val = "test"
        SetClipboardData(win32con.CF_TEXT, val)
        # Get the raw data - this will include the '\0'
        raw_data = GetGlobalMemory(GetClipboardDataHandle(win32con.CF_TEXT))
        self.failUnlessEqual(val + '\0', raw_data)
    def test_bad_mem(self):
        self.failUnlessRaises(pywintypes.error, GetGlobalMemory, 0)
        self.failUnlessRaises(pywintypes.error, GetGlobalMemory, 1)
        self.failUnlessRaises(pywintypes.error, GetGlobalMemory, -1)
    def test_custom_mem(self):
        test_data = "hello\x00\xff"
        test_buffer = array.array("c", test_data)
        cf = RegisterClipboardFormat(custom_format_name)
        self.failUnlessEqual(custom_format_name, GetClipboardFormatName(cf))
        SetClipboardData(cf, test_buffer)
        hglobal = GetClipboardDataHandle(cf)
        data = GetGlobalMemory(hglobal)
        self.failUnlessEqual(data, test_data)

if __name__ == '__main__':
    unittest.main()
