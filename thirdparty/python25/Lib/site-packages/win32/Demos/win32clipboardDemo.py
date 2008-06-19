# win32clipboardDemo.py
#
# Demo/test of the win32clipboard module.
from win32clipboard import *
import win32con
import types

if not __debug__:
    print "WARNING: The test code in this module uses assert"
    print "This instance of Python has asserts disabled, so many tests will be skipped"

cf_names = {}
# Build map of CF_* constants to names.
for name, val in win32con.__dict__.items():
    if name[:3]=="CF_" and name != "CF_SCREENFONTS": # CF_SCREEN_FONTS==CF_TEXT!?!?
        cf_names[val] = name

def TestEmptyClipboard():
    OpenClipboard()
    try:
        EmptyClipboard()
        assert EnumClipboardFormats(0)==0, "Clipboard formats were available after emptying it!"
    finally:
        CloseClipboard()

def TestText():
    OpenClipboard()
    try:
        text = "Hello from Python"
        SetClipboardText(text)
        got = GetClipboardData(win32con.CF_TEXT)
        assert  got == text, "Didnt get the correct result back - '%r'." % (got,)
        # Win32 documentation says I can get the result back as CF_UNICODE  or CF_OEMTEXT.
        # But it appears I need to close the clipboard for this to kick-in.
        # but if I attempt to, it fails!
    finally:
        CloseClipboard()

    OpenClipboard()
    try:
        got = GetClipboardData(win32con.CF_UNICODETEXT)
        assert  got == text, "Didnt get the correct result back - '%r'." % (got,)
        assert type(got)==types.UnicodeType, "Didnt get the correct result back - '%r'." % (got,)

        got = GetClipboardData(win32con.CF_OEMTEXT)
        assert  got == text, "Didnt get the correct result back - '%r'." % (got,)

        # Unicode tests
        EmptyClipboard()
        text = u"Hello from Python unicode"
        # Now set the Unicode value
        SetClipboardData(win32con.CF_UNICODETEXT, text)
        # Get it in Unicode.
        got = GetClipboardData(win32con.CF_UNICODETEXT)
        assert  got == text, "Didnt get the correct result back - '%r'." % (got,)
        assert type(got)==types.UnicodeType, "Didnt get the correct result back - '%r'." % (got,)

        # Close and open the clipboard to ensure auto-conversions take place.
    finally:
        CloseClipboard()

    OpenClipboard()
    try:

        # Make sure I can still get the text.
        got = GetClipboardData(win32con.CF_TEXT)
        assert  got == text, "Didnt get the correct result back - '%r'." % (got,)
        # Make sure we get back the correct types.
        got = GetClipboardData(win32con.CF_UNICODETEXT)
        assert type(got)==types.UnicodeType, "Didnt get the correct result back - '%r'." % (got,)
        got = GetClipboardData(win32con.CF_OEMTEXT)
        assert  got == text, "Didnt get the correct result back - '%r'." % (got,)
        print "Clipboard text tests worked correctly"
    finally:
        CloseClipboard()

def TestClipboardEnum():
    OpenClipboard()
    try:
        # Enumerate over the clipboard types
        enum = 0
        while 1:
            enum = EnumClipboardFormats(enum)
            if enum==0:
                break
            assert IsClipboardFormatAvailable(enum), "Have format, but clipboard says it is not available!"
            n = cf_names.get(enum,"")
            if not n:
                try:
                    n = GetClipboardFormatName(enum)
                except error:
                    n = "unknown (%s)" % (enum,)

            print "Have format", n
        print "Clipboard enumerator tests worked correctly"
    finally:
        CloseClipboard()

class Foo:
    def __init__(self, **kw):
        self.__dict__.update(kw)
    def __cmp__(self, other):
        return cmp(self.__dict__, other.__dict__)

def TestCustomFormat():
    OpenClipboard()
    try:
        # Just for the fun of it pickle Python objects through the clipboard
        fmt = RegisterClipboardFormat("Python Pickle Format")
        import cPickle
        pickled_object = Foo(a=1, b=2, Hi=3)
        SetClipboardData(fmt, cPickle.dumps( pickled_object ) )
        # Now read it back.
        data = GetClipboardData(fmt)
        loaded_object = cPickle.loads(data)
        assert cPickle.loads(data) == pickled_object, "Didnt get the correct data!"

        print "Clipboard custom format tests worked correctly"
    finally:
        CloseClipboard()


if __name__=='__main__':
    TestEmptyClipboard()
    TestText()
    TestCustomFormat()
    TestClipboardEnum()
    # And leave it empty at the end!
    TestEmptyClipboard()
