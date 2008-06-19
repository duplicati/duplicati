import unittest
import win32event
import time
import os
import sys

class TestWaitableTimer(unittest.TestCase):
    def testWaitableFire(self):
        h = win32event.CreateWaitableTimer(None, 0, None)
        dt = -160L # 160 ns.
        win32event.SetWaitableTimer(h, dt, 0, None, None, 0)
        rc = win32event.WaitForSingleObject(h, 1000)
        self.failUnlessEqual(rc, win32event.WAIT_OBJECT_0)

    def testWaitableTrigger(self):
        h = win32event.CreateWaitableTimer(None, 0, None)
        # for the sake of this, pass a long that doesn't fit in an int.
        dt = -2000000000L
        win32event.SetWaitableTimer(h, dt, 0, None, None, 0)
        rc = win32event.WaitForSingleObject(h, 10) # 10 ms.
        self.failUnlessEqual(rc, win32event.WAIT_TIMEOUT)

if __name__=='__main__':
    unittest.main()
