# See if we run in Unicode mode.
# This may be referenced all over the place, so we save it globally.
import win32api, win32con, __builtin__

# This doesn't seem to work correctly on NT - see bug 716708
is_platform_unicode  = 0
#is_platform_unicode = hasattr(__builtin__, "unicode") and win32api.GetVersionEx()[3] == win32con.VER_PLATFORM_WIN32_NT
default_platform_encoding = "mbcs" # Will it ever be necessary to change this?
default_scintilla_encoding = "utf-8" # Scintilla _only_ supports this ATM

del win32api, win32con, __builtin__
