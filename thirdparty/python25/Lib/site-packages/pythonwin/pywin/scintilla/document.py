import win32ui
from pywin.mfc import docview
from pywin import is_platform_unicode, default_platform_encoding, default_scintilla_encoding
from scintillacon import *
import win32con
import string
import array

ParentScintillaDocument=docview.Document
class CScintillaDocument(ParentScintillaDocument):
	"A SyntEdit document. "
	def DeleteContents(self):
		pass

	def OnOpenDocument(self, filename):
		# init data members
		#print "Opening", filename
		self.SetPathName(filename) # Must set this early!
		try:
			if is_platform_unicode:
				# Scintilla in UTF-8 mode - translate accordingly.
				import codecs
				f = codecs.open(filename, 'rb', default_platform_encoding)
			else:
				f = open(filename, 'rb')
			try:
				text = f.read()
			finally:
				f.close()
			if is_platform_unicode:
				# Translate from locale-specific (MCBS) encoding to UTF-8 for Scintilla
				text = text.encode(default_scintilla_encoding)
		except IOError:
			win32ui.MessageBox("Could not load the file from %s" % filename)
			return 0

		self._SetLoadedText(text)
##		if self.GetFirstView():
##			self.GetFirstView()._SetLoadedText(text)
##		self.SetModifiedFlag(0) # No longer dirty
		return 1

	def SaveFile(self, fileName):
		view = self.GetFirstView()
		ok = view.SaveTextFile(fileName)
		if ok:
			view.SCISetSavePoint()
		return ok

	def ApplyFormattingStyles(self):
		self._ApplyOptionalToViews("ApplyFormattingStyles")

	# #####################
	# File related functions
	# Helper to transfer text from the MFC document to the control.
	def _SetLoadedText(self, text):
		view = self.GetFirstView()
		if view.IsWindow():
			# Turn off undo collection while loading 
			view.SendScintilla(SCI_SETUNDOCOLLECTION, 0, 0)
			# Make sure the control isnt read-only
			view.SetReadOnly(0)

			doc = self
			view.SendScintilla(SCI_CLEARALL)
			view.SendMessage(SCI_ADDTEXT, buffer(text))
			view.SendScintilla(SCI_SETUNDOCOLLECTION, 1, 0)
			view.SendScintilla(win32con.EM_EMPTYUNDOBUFFER, 0, 0)

	def FinalizeViewCreation(self, view):
		pass

	def HookViewNotifications(self, view):
		parent = view.GetParentFrame()
		parent.HookNotify(ViewNotifyDelegate(self, "OnBraceMatch"), SCN_CHECKBRACE)
		parent.HookNotify(ViewNotifyDelegate(self, "OnMarginClick"), SCN_MARGINCLICK)
		parent.HookNotify(ViewNotifyDelegate(self, "OnNeedShown"), SCN_NEEDSHOWN)

		parent.HookNotify(DocumentNotifyDelegate(self, "OnSavePointReached"), SCN_SAVEPOINTREACHED)
		parent.HookNotify(DocumentNotifyDelegate(self, "OnSavePointLeft"), SCN_SAVEPOINTLEFT)
		parent.HookNotify(DocumentNotifyDelegate(self, "OnModifyAttemptRO"), SCN_MODIFYATTEMPTRO)
		# Tell scintilla what characters should abort auto-complete.
		view.SCIAutoCStops(string.whitespace+"()[]:;+-/*=\\?'!#@$%^&,<>\"'|" )

		if view != self.GetFirstView():
			view.SCISetDocPointer(self.GetFirstView().SCIGetDocPointer())


	def OnSavePointReached(self, std, extra):
		self.SetModifiedFlag(0)

	def OnSavePointLeft(self, std, extra):
		self.SetModifiedFlag(1)

	def OnModifyAttemptRO(self, std, extra):
		self.MakeDocumentWritable()

	# All Marker functions are 1 based.
	def MarkerAdd( self, lineNo, marker ):
		self.GetEditorView().SCIMarkerAdd(lineNo-1, marker)

	def MarkerCheck(self, lineNo, marker ):
		v = self.GetEditorView()
		lineNo = lineNo - 1 # Make 0 based
		markerState = v.SCIMarkerGet(lineNo)
		return markerState & (1<<marker) != 0

	def MarkerToggle( self, lineNo, marker ):
		v = self.GetEditorView()
		if self.MarkerCheck(lineNo, marker):
			v.SCIMarkerDelete(lineNo-1, marker)
		else:
			v.SCIMarkerAdd(lineNo-1, marker)
	def MarkerDelete( self, lineNo, marker ):
		self.GetEditorView().SCIMarkerDelete(lineNo-1, marker)
	def MarkerDeleteAll( self, marker ):
		self.GetEditorView().SCIMarkerDeleteAll(marker)
	def MarkerGetNext(self, lineNo, marker):
		return self.GetEditorView().SCIMarkerNext( lineNo-1, 1 << marker )+1
	def MarkerAtLine(self, lineNo, marker):
		markerState = self.GetEditorView().SCIMarkerGet(lineNo-1)
		return markerState & (1<<marker)

	# Helper for reflecting functions to views.
	def _ApplyToViews(self, funcName, *args):
		for view in self.GetAllViews():
			func = getattr(view, funcName)
			apply(func, args)
	def _ApplyOptionalToViews(self, funcName, *args):
		for view in self.GetAllViews():
			func = getattr(view, funcName, None)
			if func is not None:
				apply(func, args)
	def GetEditorView(self):
		# Find the first frame with a view,
		# then ask it to give the editor view
		# as it knows which one is "active"
		try:
			frame_gev = self.GetFirstView().GetParentFrame().GetEditorView
		except AttributeError:
			return self.GetFirstView()
		return frame_gev()

# Delegate to the correct view, based on the control that sent it.
class ViewNotifyDelegate:
	def __init__(self, doc, name):
		self.doc = doc
		self.name = name
	def __call__(self, std, extra):
		(hwndFrom, idFrom, code) = std
		for v in self.doc.GetAllViews():
			if v.GetSafeHwnd() == hwndFrom:
				return apply(getattr(v, self.name), (std, extra))

# Delegate to the document, but only from a single view (as each view sends it seperately)
class DocumentNotifyDelegate:
	def __init__(self, doc, name):
		self.doc = doc
		self.delegate = getattr(doc, name)
	def __call__(self, std, extra):
		(hwndFrom, idFrom, code) = std
		if hwndFrom == self.doc.GetEditorView().GetSafeHwnd():
				apply(self.delegate, (std, extra))
