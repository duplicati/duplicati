# Creates a task-bar icon.  Run from Python.exe to see the
# messages printed.
from win32api import *
from win32gui import *
import win32con
import sys, os

class MainWindow:
    def __init__(self):
        msg_TaskbarRestart = RegisterWindowMessage("TaskbarCreated");
        message_map = {
                msg_TaskbarRestart: self.OnRestart,
                win32con.WM_DESTROY: self.OnDestroy,
                win32con.WM_COMMAND: self.OnCommand,
                win32con.WM_USER+20 : self.OnTaskbarNotify,
        }
        # Register the Window class.
        wc = WNDCLASS()
        hinst = wc.hInstance = GetModuleHandle(None)
        wc.lpszClassName = "PythonTaskbarDemo"
        wc.style = win32con.CS_VREDRAW | win32con.CS_HREDRAW;
        wc.hCursor = LoadCursor( 0, win32con.IDC_ARROW )
        wc.hbrBackground = win32con.COLOR_WINDOW
        wc.lpfnWndProc = message_map # could also specify a wndproc.
        classAtom = RegisterClass(wc)
        # Create the Window.
        style = win32con.WS_OVERLAPPED | win32con.WS_SYSMENU
        self.hwnd = CreateWindow( classAtom, "Taskbar Demo", style, \
                0, 0, win32con.CW_USEDEFAULT, win32con.CW_USEDEFAULT, \
                0, 0, hinst, None)
        UpdateWindow(self.hwnd)
        self._DoCreateIcons()
    def _DoCreateIcons(self):
        # Try and find a custom icon
        hinst =  GetModuleHandle(None)
        iconPathName = os.path.abspath(os.path.join( os.path.split(sys.executable)[0], "pyc.ico" ))
        if not os.path.isfile(iconPathName):
            # Look in the source tree.
            iconPathName = os.path.abspath(os.path.join( os.path.split(sys.executable)[0], "..\\PC\\pyc.ico" ))
        if os.path.isfile(iconPathName):
            icon_flags = win32con.LR_LOADFROMFILE | win32con.LR_DEFAULTSIZE
            hicon = LoadImage(hinst, iconPathName, win32con.IMAGE_ICON, 0, 0, icon_flags)
        else:
            print "Can't find a Python icon file - using default"
            hicon = LoadIcon(0, win32con.IDI_APPLICATION)

        flags = NIF_ICON | NIF_MESSAGE | NIF_TIP
        nid = (self.hwnd, 0, flags, win32con.WM_USER+20, hicon, "Python Demo")
        try:
            Shell_NotifyIcon(NIM_ADD, nid)
        except error:
            # This is common when windows is starting, and this code is hit
            # before the taskbar has been created.
            print "Failed to add the taskbar icon - is explorer running?"
            # but keep running anyway - when explorer starts, we get the
            # TaskbarCreated message.

    def OnRestart(self, hwnd, msg, wparam, lparam):
        self._DoCreateIcons()

    def OnDestroy(self, hwnd, msg, wparam, lparam):
        nid = (self.hwnd, 0)
        Shell_NotifyIcon(NIM_DELETE, nid)
        PostQuitMessage(0) # Terminate the app.

    def OnTaskbarNotify(self, hwnd, msg, wparam, lparam):
        if lparam==win32con.WM_LBUTTONUP:
            print "You clicked me."
        elif lparam==win32con.WM_LBUTTONDBLCLK:
            print "You double-clicked me - goodbye"
            DestroyWindow(self.hwnd)
        elif lparam==win32con.WM_RBUTTONUP:
            print "You right clicked me."
            menu = CreatePopupMenu()
            AppendMenu( menu, win32con.MF_STRING, 1023, "Display Dialog")
            AppendMenu( menu, win32con.MF_STRING, 1024, "Say Hello")
            AppendMenu( menu, win32con.MF_STRING, 1025, "Exit program" )
            pos = GetCursorPos()
            # See http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/menus_0hdi.asp
            SetForegroundWindow(self.hwnd)
            TrackPopupMenu(menu, win32con.TPM_LEFTALIGN, pos[0], pos[1], 0, self.hwnd, None)
            PostMessage(self.hwnd, win32con.WM_NULL, 0, 0)
        return 1

    def OnCommand(self, hwnd, msg, wparam, lparam):
        id = LOWORD(wparam)
        if id == 1023:
            import win32gui_dialog
            win32gui_dialog.DemoModal()
        elif id == 1024:
            print "Hello"
        elif id == 1025:
            print "Goodbye"
            DestroyWindow(self.hwnd)
        else:
            print "Unknown command -", id

def main():
    w=MainWindow()
    PumpMessages()

if __name__=='__main__':
    main()
