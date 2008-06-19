from dbgcon import *
from pywin.mfc import dialog

class DebuggerOptionsPropPage(dialog.PropertyPage):
	def __init__(self):
		dialog.PropertyPage.__init__(self, win32ui.IDD_PP_DEBUGGER)

	def OnInitDialog(self):
		options = self.options = LoadDebuggerOptions()
		self.AddDDX(win32ui.IDC_CHECK1, OPT_HIDE)
		self[OPT_STOP_EXCEPTIONS] = options[OPT_STOP_EXCEPTIONS]
		self.AddDDX(win32ui.IDC_CHECK2, OPT_STOP_EXCEPTIONS)
		self[OPT_HIDE] = options[OPT_HIDE]
		return dialog.PropertyPage.OnInitDialog(self)

	def OnOK(self):
		self.UpdateData()
		dirty = 0
		for key, val in self.items():
			if self.options.has_key(key):
				if self.options[key] != val:
					self.options[key] = val
					dirty = 1
		if dirty:
			SaveDebuggerOptions(self.options)
		# If there is a debugger open, then set its options.
		import pywin.debugger
		if pywin.debugger.currentDebugger is not None:
			pywin.debugger.currentDebugger.options = self.options
		return 1
