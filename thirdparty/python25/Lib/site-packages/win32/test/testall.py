import sys, os
import unittest

def import_all():
    # Some hacks for import order - dde depends on win32ui
    import win32ui
    
    import win32api
    dir = os.path.dirname(win32api.__file__)
    num = 0
    is_debug = os.path.basename(win32api.__file__).endswith("_d")
    for name in os.listdir(dir):
        base, ext = os.path.splitext(name)
        if (ext==".pyd") and \
           name != "_winxptheme.pyd" and \
           (is_debug and base.endswith("_d") or \
           not is_debug and not base.endswith("_d")):
            try:
                __import__(base)
            except ImportError:
                print "FAILED to import", name
                raise
            num += 1

def suite():
    # Loop over all .py files here, except me :)
    try:
        me = __file__
    except NameError:
        me = sys.argv[0]
    me = os.path.abspath(me)
    files = os.listdir(os.path.dirname(me))
    suite = unittest.TestSuite()
    suite.addTest(unittest.FunctionTestCase(import_all))
    for file in files:
        base, ext = os.path.splitext(file)
        if ext=='.py' and os.path.basename(me) != file:
            mod = __import__(base)
            if hasattr(mod, "suite"):
                test = mod.suite()
            else:
                test = unittest.defaultTestLoader.loadTestsFromModule(mod)
            suite.addTest(test)
    return suite

class CustomLoader(unittest.TestLoader):
    def loadTestsFromModule(self, module):
        return suite()

if __name__=='__main__':
    unittest.TestProgram(testLoader=CustomLoader())(argv=sys.argv)
