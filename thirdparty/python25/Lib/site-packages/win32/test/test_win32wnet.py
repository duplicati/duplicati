import unittest
import win32wnet, win32api

class TestCase(unittest.TestCase):
    def testGetUser(self):
        self.assertEquals(win32api.GetUserName(), win32wnet.WNetGetUser())


if __name__ == '__main__':
    unittest.main()
