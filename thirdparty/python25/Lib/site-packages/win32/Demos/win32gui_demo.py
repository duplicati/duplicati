# The start of a win32gui generic demo.
# Feel free to contribute more demos back ;-)

import win32gui

def _MyCallback( hwnd, extra ):
    hwnds, classes = extra
    hwnds.append(hwnd)
    classes[win32gui.GetClassName(hwnd)] = 1

def TestEnumWindows():
    windows = []
    classes = {}
    win32gui.EnumWindows(_MyCallback, (windows, classes))
    print "Enumerated a total of %d windows with %d classes" % (len(windows),len(classes))
    if not classes.has_key("tooltips_class32"):
        print "Hrmmmm - I'm very surprised to not find a 'tooltips_class32' class."

print "Enumerating all windows..."
TestEnumWindows()
print "All tests done!"
