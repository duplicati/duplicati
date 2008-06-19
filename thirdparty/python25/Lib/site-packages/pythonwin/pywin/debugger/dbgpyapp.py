# dbgpyapp.py  - Debugger Python application class
#
import win32con
import win32ui
import sys
import string
import os
from pywin.framework import intpyapp

version = '0.3.0'

class DebuggerPythonApp(intpyapp.InteractivePythonApp):
	def LoadMainFrame(self):
		" Create the main applications frame "
		self.frame = self.CreateMainFrame()
		self.SetMainFrame(self.frame)
		self.frame.LoadFrame(win32ui.IDR_DEBUGGER, win32con.WS_OVERLAPPEDWINDOW)
		self.frame.DragAcceptFiles()	# we can accept these.
		self.frame.ShowWindow(win32con.SW_HIDE);
		self.frame.UpdateWindow();

		# but we do rehook, hooking the new code objects.
		self.HookCommands()

	def InitInstance(self):
		# Use a registry path of "Python\Pythonwin Debugger
		win32ui.SetAppName(win32ui.LoadString(win32ui.IDR_DEBUGGER))
		win32ui.SetRegistryKey("Python %s" % (sys.winver,))
		# We _need_ the Scintilla color editor.
		# (and we _always_ get it now :-)

		numMRU = win32ui.GetProfileVal("Settings","Recent File List Size", 10)
		win32ui.LoadStdProfileSettings(numMRU)

		self.LoadMainFrame()

		# Display the interactive window if the user wants it.
		from pywin.framework import interact
		interact.CreateInteractiveWindowUserPreference()

		# Load the modules we use internally.
		self.LoadSystemModules()
		# Load additional module the user may want.
		self.LoadUserModules()

#		win32ui.CreateDebuggerThread()
		win32ui.EnableControlContainer()

		# Load the ToolBar state near the end of the init process, as
		# there may be Toolbar IDs created by the user or other modules.
		# By now all these modules should be loaded, so all the toolbar IDs loaded.
		try:
			self.frame.LoadBarState("ToolbarDefault")
		except win32ui.error:
			# MFC sucks.  It does essentially "GetDlgItem(x)->Something", so if the
			# toolbar with ID x does not exist, MFC crashes!  Pythonwin has a trap for this
			# but I need to investigate more how to prevent it (AFAIK, ensuring all the
			# toolbars are created by now _should_ stop it!)
			pass
