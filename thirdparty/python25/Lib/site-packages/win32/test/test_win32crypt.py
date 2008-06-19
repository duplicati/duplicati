# Test module for win32timezone

import unittest
import win32crypt

class Crypt(unittest.TestCase):
    def testSimple(self):
        data = "My test data"
        entropy = None
        desc = "My description"
        flags = 0
        ps = None
        blob = win32crypt.CryptProtectData(data, desc, entropy, None, ps, flags)
        got_desc, got_data = win32crypt.CryptUnprotectData(blob, entropy, None, ps, flags)
        self.failUnlessEqual(data, got_data)
        self.failUnlessEqual(desc, got_desc)

    def testEntropy(self):
        data = "My test data"
        entropy = "My test entropy"
        desc = "My description"
        flags = 0
        ps = None
        blob = win32crypt.CryptProtectData(data, desc, entropy, None, ps, flags)
        got_desc, got_data = win32crypt.CryptUnprotectData(blob, entropy, None, ps, flags)
        self.failUnlessEqual(data, got_data)
        self.failUnlessEqual(desc, got_desc)

if __name__ == '__main__':
    unittest.main()
