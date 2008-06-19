import unittest
import win32api, win32file, win32pipe, pywintypes, winerror, win32event
import win32con, ntsecuritycon
import sys
import os
import tempfile
import sets
import threading
import time
import shutil

class TestSimpleOps(unittest.TestCase):
    def testSimpleFiles(self):
        try:
            fd, filename = tempfile.mkstemp()
        except AttributeError:
            self.fail("This test requires Python 2.3 or later")
        os.close(fd)
        os.unlink(filename)
        handle = win32file.CreateFile(filename, win32file.GENERIC_WRITE, 0, None, win32con.CREATE_NEW, 0, None)
        test_data = "Hello\0there"
        try:
            win32file.WriteFile(handle, test_data)
            handle.Close()
            # Try and open for read
            handle = win32file.CreateFile(filename, win32file.GENERIC_READ, 0, None, win32con.OPEN_EXISTING, 0, None)
            rc, data = win32file.ReadFile(handle, 1024)
            self.assertEquals(data, test_data)
        finally:
            handle.Close()
            try:
                os.unlink(filename)
            except os.error:
                pass

    # A simple test using normal read/write operations.
    def testMoreFiles(self):
        # Create a file in the %TEMP% directory.
        testName = os.path.join( win32api.GetTempPath(), "win32filetest.dat" )
        desiredAccess = win32file.GENERIC_READ | win32file.GENERIC_WRITE
        # Set a flag to delete the file automatically when it is closed.
        fileFlags = win32file.FILE_FLAG_DELETE_ON_CLOSE
        h = win32file.CreateFile( testName, desiredAccess, win32file.FILE_SHARE_READ, None, win32file.CREATE_ALWAYS, fileFlags, 0)
    
        # Write a known number of bytes to the file.
        data = "z" * 1025
    
        win32file.WriteFile(h, data)
    
        self.failUnless(win32file.GetFileSize(h) == len(data), "WARNING: Written file does not have the same size as the length of the data in it!")
    
        # Ensure we can read the data back.
        win32file.SetFilePointer(h, 0, win32file.FILE_BEGIN)
        hr, read_data = win32file.ReadFile(h, len(data)+10) # + 10 to get anything extra
        self.failUnless(hr==0, "Readfile returned %d" % hr)

        self.failUnless(read_data == data, "Read data is not what we wrote!")
    
        # Now truncate the file at 1/2 its existing size.
        newSize = len(data)/2
        win32file.SetFilePointer(h, newSize, win32file.FILE_BEGIN)
        win32file.SetEndOfFile(h)
        self.failUnless(win32file.GetFileSize(h) == newSize, "Truncated file does not have the expected size!")
    
        # GetFileAttributesEx/GetFileAttributesExW tests.
        self.failUnless(win32file.GetFileAttributesEx(testName) == win32file.GetFileAttributesExW(testName),
                        "ERROR: Expected GetFileAttributesEx and GetFileAttributesExW to return the same data")
    
        attr, ct, at, wt, size = win32file.GetFileAttributesEx(testName)
        self.failUnless(size==newSize, 
                        "Expected GetFileAttributesEx to return the same size as GetFileSize()")
        self.failUnless(attr==win32file.GetFileAttributes(testName), 
                        "Expected GetFileAttributesEx to return the same attributes as GetFileAttributes")
    
        h = None # Close the file by removing the last reference to the handle!

        self.failUnless(not os.path.isfile(testName), "After closing the file, it still exists!")

    def testFilePointer(self):
        # via [ 979270 ] SetFilePointer fails with negative offset

        # Create a file in the %TEMP% directory.
        filename = os.path.join( win32api.GetTempPath(), "win32filetest.dat" )

        f = win32file.CreateFile(filename,
                                win32file.GENERIC_READ|win32file.GENERIC_WRITE,
                                0,
                                None,
                                win32file.CREATE_ALWAYS,
                                win32file.FILE_ATTRIBUTE_NORMAL,
                                0)
        try:
            #Write some data
            data = 'Some data'
            (res, written) = win32file.WriteFile(f, data)
            
            self.failIf(res)
            self.assertEqual(written, len(data))
            
            #Move at the beginning and read the data
            win32file.SetFilePointer(f, 0, win32file.FILE_BEGIN)
            (res, s) = win32file.ReadFile(f, len(data))
            
            self.failIf(res)
            self.assertEqual(s, data)
            
            #Move at the end and read the data
            win32file.SetFilePointer(f, -len(data), win32file.FILE_END)
            (res, s) = win32file.ReadFile(f, len(data))
            
            self.failIf(res)
            self.failUnlessEqual(s, data)
        finally:
            f.Close()
            os.unlink(filename)

class TestOverlapped(unittest.TestCase):
    def testSimpleOverlapped(self):
        # Create a file in the %TEMP% directory.
        import win32event
        testName = os.path.join( win32api.GetTempPath(), "win32filetest.dat" )
        desiredAccess = win32file.GENERIC_WRITE
        overlapped = pywintypes.OVERLAPPED()
        evt = win32event.CreateEvent(None, 0, 0, None)
        overlapped.hEvent = evt
        # Create the file and write shit-loads of data to it.
        h = win32file.CreateFile( testName, desiredAccess, 0, None, win32file.CREATE_ALWAYS, 0, 0)
        chunk_data = "z" * 0x8000
        num_loops = 512
        expected_size = num_loops * len(chunk_data)
        for i in range(num_loops):
            win32file.WriteFile(h, chunk_data, overlapped)
            win32event.WaitForSingleObject(overlapped.hEvent, win32event.INFINITE)
            overlapped.Offset = overlapped.Offset + len(chunk_data)
        h.Close()
        # Now read the data back overlapped
        overlapped = pywintypes.OVERLAPPED()
        evt = win32event.CreateEvent(None, 0, 0, None)
        overlapped.hEvent = evt
        desiredAccess = win32file.GENERIC_READ
        h = win32file.CreateFile( testName, desiredAccess, 0, None, win32file.OPEN_EXISTING, 0, 0)
        buffer = win32file.AllocateReadBuffer(0xFFFF)
        while 1:
            try:
                hr, data = win32file.ReadFile(h, buffer, overlapped)
                win32event.WaitForSingleObject(overlapped.hEvent, win32event.INFINITE)
                overlapped.Offset = overlapped.Offset + len(data)
                if not data is buffer:
                    self.fail("Unexpected result from ReadFile - should be the same buffer we passed it")
            except win32api.error:
                break
        h.Close()

    def testCompletionPortsMultiple(self):
        # Mainly checking that we can "associate" an existing handle.  This
        # failed in build 203.
        import socket

        ioport = win32file.CreateIoCompletionPort(win32file.INVALID_HANDLE_VALUE,
                                                  0, 0, 0)
        socks = []
        for PORT in range(9123, 9125):
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            sock.bind(('', PORT))
            sock.listen(1)
            socks.append(sock)
            new = win32file.CreateIoCompletionPort(sock.fileno(), ioport, PORT, 0)
            assert new is ioport
        for s in socks:
            s.close()
        hv = int(ioport)
        ioport = new = None
        # The handle itself should be closed now (unless we leak references!)
        # Check that.
        try:
            win32file.CloseHandle(hv)
            raise RuntimeError, "Expected close to fail!"
        except win32file.error, (hr, func, msg):
            self.failUnlessEqual(hr, winerror.ERROR_INVALID_HANDLE)

    def testCompletionPortsQueued(self):
        class Foo: pass
        io_req_port = win32file.CreateIoCompletionPort(-1, None, 0, 0)
        overlapped = pywintypes.OVERLAPPED()
        overlapped.object = Foo()
        win32file.PostQueuedCompletionStatus(io_req_port, 0, 99, overlapped)
        errCode, bytes, key, overlapped = \
                win32file.GetQueuedCompletionStatus(io_req_port, win32event.INFINITE)
        self.failUnlessEqual(errCode, 0)
        self.failUnless(isinstance(overlapped.object, Foo))

    def _IOCPServerThread(self, handle, port, drop_overlapped_reference):
        overlapped = pywintypes.OVERLAPPED()
        win32pipe.ConnectNamedPipe(handle, overlapped)
        if drop_overlapped_reference:
            # Be naughty - the overlapped object is now dead, but
            # GetQueuedCompletionStatus will still find it.  Our check of
            # reference counting should catch that error.
            overlapped = None
            self.failUnlessRaises(RuntimeError,
                                  win32file.GetQueuedCompletionStatus, port, -1)
            handle.Close()
            return

        result = win32file.GetQueuedCompletionStatus(port, -1)
        ol2 = result[-1]
        self.failUnless(ol2 is overlapped)
        data = win32file.ReadFile(handle, 512)[1]
        win32file.WriteFile(handle, data)

    def testCompletionPortsNonQueued(self, test_overlapped_death = 0):
        # In 204 we had a reference count bug when OVERLAPPED objects were
        # associated with a completion port other than via
        # PostQueuedCompletionStatus.  This test is based on the reproduction
        # reported with that bug.
        # Create the pipe.
        BUFSIZE = 512
        pipe_name = r"\\.\pipe\pywin32_test_pipe"
        handle = win32pipe.CreateNamedPipe(pipe_name,
                          win32pipe.PIPE_ACCESS_DUPLEX|
                          win32file.FILE_FLAG_OVERLAPPED,
                          win32pipe.PIPE_TYPE_MESSAGE|
                          win32pipe.PIPE_READMODE_MESSAGE|
                          win32pipe.PIPE_WAIT,
                          1, BUFSIZE, BUFSIZE,
                          win32pipe.NMPWAIT_WAIT_FOREVER,
                          None)
        # Create an IOCP and associate it with the handle.        
        port = win32file.CreateIoCompletionPort(-1, 0, 0, 0)
        win32file.CreateIoCompletionPort(handle, port, 1, 0)

        thread = threading.Thread(target=self._IOCPServerThread, args=(handle,port, test_overlapped_death))
        thread.start()
        try:
            time.sleep(0.1) # let thread do its thing.
            try:
                win32pipe.CallNamedPipe(r"\\.\pipe\pywin32_test_pipe", "Hello there", BUFSIZE, 0)
            except win32pipe.error:
                # Testing for overlapped death causes this
                if not test_overlapped_death:
                    raise
        finally:
            handle.Close()
            thread.join()

    def testCompletionPortsNonQueuedBadReference(self):
        self.testCompletionPortsNonQueued(True)

    def testHashable(self):
        overlapped = pywintypes.OVERLAPPED()
        d = {}
        d[overlapped] = "hello"

class TestFindFiles(unittest.TestCase):
    def testIter(self):
        dir = os.path.join(os.getcwd(), "*")
        files = win32file.FindFilesW(dir)
        set1 = sets.Set()
        set1.update(files)
        set2 = sets.Set()
        for file in win32file.FindFilesIterator(dir):
            set2.add(file)
        assert len(set2) > 5, "This directory has less than 5 files!?"
        self.failUnlessEqual(set1, set2)

    def testBadDir(self):
        dir = os.path.join(os.getcwd(), "a dir that doesnt exist", "*")
        self.assertRaises(win32file.error, win32file.FindFilesIterator, dir)

    def testEmptySpec(self):
        spec = os.path.join(os.getcwd(), "*.foo_bar")
        num = 0
        for i in win32file.FindFilesIterator(spec):
            num += 1
        self.failUnlessEqual(0, num)

    def testEmptyDir(self):
        test_path = os.path.join(win32api.GetTempPath(), "win32file_test_directory")
        try:
            # Note: previously used shutil.rmtree, but when looking for
            # reference count leaks, that function showed leaks!  os.rmdir
            # doesn't have that problem.
            os.rmdir(test_path)
        except os.error:
            pass
        os.mkdir(test_path)
        try:
            num = 0
            for i in win32file.FindFilesIterator(os.path.join(test_path, "*")):
                num += 1
            # Expecting "." and ".." only
            self.failUnlessEqual(2, num)
        finally:
            os.rmdir(test_path)

class TestDirectoryChanges(unittest.TestCase):
    num_test_dirs = 1
    def setUp(self):
        self.watcher_threads = []
        self.watcher_thread_changes = []
        self.dir_names = []
        self.dir_handles = []
        for i in range(self.num_test_dirs):
            td = tempfile.mktemp("-test-directory-changes-%d" % i)
            os.mkdir(td)
            self.dir_names.append(td)
            hdir = win32file.CreateFile(td, 
                                        ntsecuritycon.FILE_LIST_DIRECTORY,
                                        win32con.FILE_SHARE_READ,
                                        None, # security desc
                                        win32con.OPEN_EXISTING,
                                        win32con.FILE_FLAG_BACKUP_SEMANTICS |
                                        win32con.FILE_FLAG_OVERLAPPED,
                                        None)
            self.dir_handles.append(hdir)

            changes = []
            t = threading.Thread(target=self._watcherThreadOverlapped,
                                 args=(td, hdir, changes))
            t.start()
            self.watcher_threads.append(t)
            self.watcher_thread_changes.append(changes)

    def _watcherThread(self, dn, dh, changes):
        # A synchronous version:
        # XXX - not used - I was having a whole lot of problems trying to
        # get this to work.  Specifically:
        # * ReadDirectoryChangesW without an OVERLAPPED blocks infinitely.
        # * If another thread attempts to close the handle while
        #   ReadDirectoryChangesW is waiting on it, the ::CloseHandle() method
        #   blocks (which has nothing to do with the GIL - it is correctly
        #   managed)
        # Which ends up with no way to kill the thread!
        flags = win32con.FILE_NOTIFY_CHANGE_FILE_NAME
        while 1:
            try:
                print "waiting", dh
                changes = win32file.ReadDirectoryChangesW(dh,
                                                          8192,
                                                          False, #sub-tree
                                                          flags)
                print "got", changes
            except 'xx':
                xx
            changes.extend(changes)

    def _watcherThreadOverlapped(self, dn, dh, changes):
        flags = win32con.FILE_NOTIFY_CHANGE_FILE_NAME
        buf = win32file.AllocateReadBuffer(8192)
        overlapped = pywintypes.OVERLAPPED()
        overlapped.hEvent = win32event.CreateEvent(None, 0, 0, None)
        while 1:
            win32file.ReadDirectoryChangesW(dh,
                                            buf,
                                            False, #sub-tree
                                            flags,
                                            overlapped)
            # Wait for our event, or for 5 seconds.
            rc = win32event.WaitForSingleObject(overlapped.hEvent, 5000)
            if rc == win32event.WAIT_OBJECT_0:
                # got some data!  Must use GetOverlappedResult to find out
                # how much is valid!  0 generally means the handle has
                # been closed.  Blocking is OK here, as the event has
                # already been set.
                nbytes = win32file.GetOverlappedResult(dh, overlapped, True)
                if nbytes:
                    bits = win32file.FILE_NOTIFY_INFORMATION(buf, nbytes)
                    changes.extend(bits)
                else:
                    # This is "normal" exit - our 'tearDown' closes the
                    # handle.
                    # print "looks like dir handle was closed!"
                    return
            else:
                print "ERROR: Watcher thread timed-out!"
                return # kill the thread!

    def tearDown(self):
        # be careful about raising errors at teardown!
        for h in self.dir_handles:
            # See comments in _watcherThread above - this appears to
            # deadlock if a synchronous ReadDirectoryChangesW is waiting...
            # (No such problems with an asynch ReadDirectoryChangesW)
            h.Close()
        for dn in self.dir_names:
            try:
                shutil.rmtree(dn)
            except OSError:
                print "FAILED to remove directory", dn

        for t in self.watcher_threads:
            # closing dir handle should have killed threads!
            t.join(5)
            if t.isAlive():
                print "FAILED to wait for thread termination"

    def stablize(self):
        time.sleep(0.5)

    def testSimple(self):
        self.stablize()
        for dn in self.dir_names:
            fn = os.path.join(dn, "test_file")
            open(fn, "w").close()

        self.stablize()
        changes = self.watcher_thread_changes[0]
        self.failUnlessEqual(changes, [(1, "test_file")])

class TestEncrypt(unittest.TestCase):
    def testEncrypt(self):
        fname = tempfile.mktemp("win32file_test")
        f = open(fname, "wb")
        f.write("hello")
        f.close()
        f = None
        try:
            try:
                win32file.EncryptFile(fname)
            except win32file.error, details:
                if details[0] != winerror.ERROR_ACCESS_DENIED:
                    raise
                print "It appears this is not NTFS - cant encrypt/decrypt"
            win32file.DecryptFile(fname)
        finally:
            if f is not None:
                f.close()
            os.unlink(fname)

if __name__ == '__main__':
    unittest.main()
