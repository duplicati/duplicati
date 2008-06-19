import unittest
import pywintypes
import win32api

# A class that will never die vie refcounting, but will die via GC.
class Cycle:
    def __init__(self, handle):
        self.cycle = self
        self.handle = handle

class PyHandleTestCase(unittest.TestCase):
    def testCleanup1(self):
        # We used to clobber all outstanding exceptions.
        def f1(invalidate):
            import win32event
            h = win32event.CreateEvent(None, 0, 0, None)
            if invalidate:
                win32api.CloseHandle(int(h))
            1/0
            # If we invalidated, then the object destruction code will attempt 
            # to close an invalid handle.  We don't wan't an exception in 
            # this case

        def f2(invalidate):
            """ This function should throw an IOError. """
            try:
                f1(invalidate)
            except ZeroDivisionError, exc:
                raise IOError("raise 2")

        self.assertRaises(IOError, f2, False)
        # Now do it again, but so the auto object destruction
        # actually fails.
        self.assertRaises(IOError, f2, True)

    def testCleanup2(self):
        # Cause an exception during object destruction.
        # The worst this does is cause an ".XXX undetected error (why=3)" 
        # So avoiding that is the goal
        import win32event
        h = win32event.CreateEvent(None, 0, 0, None)
        # Close the handle underneath the object.
        win32api.CloseHandle(int(h))
        # Object destructor runs with the implicit close failing
        h = None

    def testCleanup3(self):
        # And again with a class - no __del__
        import win32event
        class Test:
            def __init__(self):
                self.h = win32event.CreateEvent(None, 0, 0, None)
                win32api.CloseHandle(int(self.h))
        t=Test()
        t = None

    def testCleanupGood(self):
        # And check that normal error semantics *do* work.
        import win32event
        h = win32event.CreateEvent(None, 0, 0, None)
        win32api.CloseHandle(int(h))
        self.assertRaises(win32api.error, h.Close)
        # A following Close is documented as working
        h.Close()

    def testInvalid(self):
        h=pywintypes.HANDLE(-2)
        self.assertRaises(win32api.error, h.Close)

    def testGC(self):
        # This used to provoke:
        # Fatal Python error: unexpected exception during garbage collection
        def make():
            h=pywintypes.HANDLE(-2)
            c = Cycle(h)
        import gc
        make()
        gc.collect()

    def testTypes(self):
        self.assertRaises(TypeError, pywintypes.HANDLE, "foo")
        self.assertRaises(TypeError, pywintypes.HANDLE, ())
        # should be able to get a long!
        pywintypes.HANDLE(0L)

if __name__ == '__main__':
    unittest.main()
