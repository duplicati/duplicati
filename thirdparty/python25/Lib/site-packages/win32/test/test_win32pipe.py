import unittest


import win32pipe
import win32file
import pywintypes
import winerror
import threading

class CurrentUserTestCase(unittest.TestCase):
    pipename = "\\\\.\\pipe\\python_test_pipe"
    def _workerThread(self, e):
        data = win32pipe.CallNamedPipe(self.pipename,"foo\0bar", 1024, win32pipe.NMPWAIT_WAIT_FOREVER)
        e.set()
        self.failUnless(data == "bar\0foo")

    def testCallNamedPipe(self):
        openMode = win32pipe.PIPE_ACCESS_DUPLEX
        pipeMode = win32pipe.PIPE_TYPE_MESSAGE  | win32pipe.PIPE_WAIT
    
        sa = pywintypes.SECURITY_ATTRIBUTES()
        sa.SetSecurityDescriptorDacl ( 1, None, 0 )
    
        pipeHandle = win32pipe.CreateNamedPipe(self.pipename,
                                                openMode,
                                                pipeMode,
                                                win32pipe.PIPE_UNLIMITED_INSTANCES,
                                                0,
                                                0,
                                                2000,
                                                sa)
    
        event = threading.Event()
        threading.Thread(target=self._workerThread, args=(event,)).start()
    
        hr = win32pipe.ConnectNamedPipe(pipeHandle)
        self.failUnlessEqual(0, hr)
        win32file.WriteFile(pipeHandle, "bar\0foo")
        hr, got = win32file.ReadFile(pipeHandle, 100)
        self.failUnless(got == "foo\0bar")
        event.wait(5)
        if not event.isSet():
            self.fail("Failed to wait for event!")

if __name__ == '__main__':
    unittest.main()
