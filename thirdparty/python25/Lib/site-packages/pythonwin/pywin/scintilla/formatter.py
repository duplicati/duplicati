# Does Python source formatting for Scintilla controls.
import win32ui
import win32api
import win32con
import winerror
import string
import array
import scintillacon

WM_KICKIDLE = 0x036A

debugging = 0
if debugging:
	# Output must go to another process else the result of
	# the printing itself will trigger again trigger a trace.
	import sys, win32traceutil, win32trace 
	def trace(*args):
		win32trace.write(string.join(map(str, args), " ") + "\n")
else:
	trace = lambda *args: None

class Style:
	"""Represents a single format
	"""
	def __init__(self, name, format, background = None):
		self.name = name # Name the format representes eg, "String", "Class"
		self.background = background
		if type(format)==type(''):
			self.aliased = format
			self.format = None
		else:
			self.format = format
			self.aliased = None
		self.stylenum = None # Not yet registered.
	def IsBasedOnDefault(self):
		return len(self.format)==5
	# If the currently extended font defintion matches the
	# default format, restore the format to the "simple" format.
	def NormalizeAgainstDefault(self, defaultFormat):
		if self.IsBasedOnDefault():
			return 0 # No more to do, and not changed.
		bIsDefault = self.format[7] == defaultFormat[7] and \
		             self.format[2] == defaultFormat[2]
		if bIsDefault:
			self.ForceAgainstDefault()
		return bIsDefault
	def ForceAgainstDefault(self):
		self.format = self.format[:5]
	def GetCompleteFormat(self, defaultFormat):
		# Get the complete style after applying any relevant defaults.
		if len(self.format)==5: # It is a default one
			fmt = self.format + defaultFormat[5:]
		else:
			fmt = self.format
		flags = win32con.CFM_BOLD | win32con.CFM_CHARSET | win32con.CFM_COLOR | win32con.CFM_FACE | win32con.CFM_ITALIC | win32con.CFM_SIZE
		return (flags,) + fmt[1:]

# The Formatter interface
# used primarily when the actual formatting is done by Scintilla!
class FormatterBase:
	def __init__(self, scintilla):
		self.scintilla = scintilla
		self.baseFormatFixed = (-402653169, 0, 200, 0, 0, 0, 49, 'Courier New')
		self.baseFormatProp = (-402653169, 0, 200, 0, 0, 0, 49, 'Arial')
		self.bUseFixed = 1
		self.styles = {} # Indexed by name
		self.styles_by_id = {} # Indexed by allocated ID.
		# Default Background
		self.default_background = None
		self._LoadBackground()

		self.SetStyles()
	

	def _LoadBackground( self ):
		#load default background
		bg = int( self.LoadPreference( "Default Background", -1 ) )
		if bg != -1:
			self.default_background = bg
		if self.default_background is None:
			self.default_background = win32api.RGB( 0xff, 0xff, 0xff )

	def GetDefaultBackground( self ):
		return self.default_background

	def HookFormatter(self, parent = None):
		raise NotImplementedError

	# Used by the IDLE extensions to quickly determine if a character is a string.
	def GetStringStyle(self, pos):
		try:
			style = self.styles_by_id[self.scintilla.SCIGetStyleAt(pos)]
		except KeyError:
			# A style we dont know about - probably not even a .py file - can't be a string
			return None			
		if style.name in self.string_style_names:
			return style
		return None

	def RegisterStyle(self, style, stylenum):
		assert stylenum is not None, "We must have a style number"
		assert style.stylenum is None, "Style has already been registered"
		assert not self.styles.has_key(stylenum), "We are reusing a style number!"
		style.stylenum = stylenum
		self.styles[style.name] = style
		self.styles_by_id[stylenum] = style

	def SetStyles(self):
		raise NotImplementedError

	def GetSampleText(self):
		return "Sample Text for the Format Dialog"

	def GetDefaultFormat(self):
			if self.bUseFixed:
				return self.baseFormatFixed
			return self.baseFormatProp

	# Update the control with the new style format.
	def _ReformatStyle(self, style):
		assert style.stylenum is not None, "Unregistered style."
		#print "Reformat style", style.name, style.stylenum
		scintilla=self.scintilla
		stylenum = style.stylenum
		# Now we have the style number, indirect for the actual style.
		if style.aliased is not None:
			style = self.styles[style.aliased]
		f=style.format
		if style.IsBasedOnDefault():
			baseFormat = self.GetDefaultFormat()
		else: baseFormat = f
		scintilla.SCIStyleSetFore(stylenum, f[4])
		scintilla.SCIStyleSetFont(stylenum, baseFormat[7], baseFormat[5])
		if f[1] & 1: scintilla.SCIStyleSetBold(stylenum, 1)
		else: scintilla.SCIStyleSetBold(stylenum, 0)
		if f[1] & 2: scintilla.SCIStyleSetItalic(stylenum, 1)
		else: scintilla.SCIStyleSetItalic(stylenum, 0)
		scintilla.SCIStyleSetSize(stylenum, int(baseFormat[2]/20))
		if style.background is not None:
			scintilla.SCIStyleSetBack(stylenum, style.background)
		else:
			scintilla.SCIStyleSetBack(stylenum, self.GetDefaultBackground() )
		scintilla.SCIStyleSetEOLFilled(stylenum, 1) # Only needed for unclosed strings.

	def GetStyleByNum(self, stylenum):
		return self.styles_by_id[stylenum]

	def ApplyFormattingStyles(self, bReload=1):
		if bReload:
			self.LoadPreferences()
		baseFormat = self.GetDefaultFormat()
		defaultStyle = Style("default", baseFormat)
		defaultStyle.stylenum = scintillacon.STYLE_DEFAULT
		self._ReformatStyle(defaultStyle)
		for style in self.styles.values():
			if style.aliased is None:
				style.NormalizeAgainstDefault(baseFormat)
			self._ReformatStyle(style)
		self.scintilla.InvalidateRect()

	# Some functions for loading and saving preferences.  By default
	# an INI file (well, MFC maps this to the registry) is used.
	def LoadPreferences(self):
		self.baseFormatFixed = eval(self.LoadPreference("Base Format Fixed", str(self.baseFormatFixed)))
		self.baseFormatProp = eval(self.LoadPreference("Base Format Proportional", str(self.baseFormatProp)))
		self.bUseFixed = int(self.LoadPreference("Use Fixed", 1))

		for style in self.styles.values():
			new = self.LoadPreference(style.name, str(style.format))
			try:
				style.format = eval(new)
				bg = int(self.LoadPreference(style.name + " background", -1))
				if bg != -1:
					style.background = bg
				if style.background == self.default_background:
					style.background = None
					
			except:
				print "Error loading style data for", style.name

	def LoadPreference(self, name, default):
		return win32ui.GetProfileVal("Format", name, default)

	def SavePreferences(self):
		self.SavePreference("Base Format Fixed", str(self.baseFormatFixed))
		self.SavePreference("Base Format Proportional", str(self.baseFormatProp))
		self.SavePreference("Use Fixed", self.bUseFixed)
		for style in self.styles.values():
			if style.aliased is None:
				self.SavePreference(style.name, str(style.format))
				if style.background is not None:
					bg_name = style.name + " background"
					self.SavePreference(bg_name, style.background) # May be None
					
	def SavePreference(self, name, value):
		if value is None:
			hkey = win32ui.GetAppRegistryKey()
			try:
				subkey = win32api.RegOpenKey(hkey, "Format", 0, win32con.KEY_SET_VALUE)
			except win32api.error, (rc, fn, msg):
				if rc != winerror.ERROR_FILE_NOT_FOUND:
					raise
			subkey.Close()
		else:
			win32ui.WriteProfileVal("Format", name, value)

# An abstract formatter
# For all formatters we actually implement here.
# (as opposed to those formatters built in to Scintilla)
class Formatter(FormatterBase):
	def __init__(self, scintilla):
		self.bCompleteWhileIdle = 0
		self.bHaveIdleHandler = 0 # Dont currently have an idle handle
		self.nextstylenum = 0
		FormatterBase.__init__(self, scintilla)

	def HookFormatter(self, parent = None):
		if parent is None: parent = self.scintilla.GetParent() # was GetParentFrame()!?
		parent.HookNotify(self.OnStyleNeeded, scintillacon.SCN_STYLENEEDED)

	def OnStyleNeeded(self, std, extra):
		notify = self.scintilla.SCIUnpackNotifyMessage(extra)
		endStyledChar = self.scintilla.SendScintilla(scintillacon.SCI_GETENDSTYLED)
		lineEndStyled = self.scintilla.LineFromChar(endStyledChar)
		endStyled = self.scintilla.LineIndex(lineEndStyled)
		#print "enPosPaint %d endStyledChar %d lineEndStyled %d endStyled %d" % (endPosPaint, endStyledChar, lineEndStyled, endStyled)
		self.Colorize(endStyled, notify.position)

	def ColorSeg(self, start, end, styleName):
		end = end+1
#		assert end-start>=0, "Can't have negative styling"
		stylenum = self.styles[styleName].stylenum
		while start<end:
			self.style_buffer[start]=chr(stylenum)
			start = start+1
		#self.scintilla.SCISetStyling(end - start + 1, stylenum)

	def RegisterStyle(self, style, stylenum = None):
		if stylenum is None:
			stylenum = self.nextstylenum
			self.nextstylenum = self.nextstylenum + 1
		FormatterBase.RegisterStyle(self, style, stylenum)

	def ColorizeString(self, str, charStart, styleStart):
		raise RuntimeError, "You must override this method"

	def Colorize(self, start=0, end=-1):
		scintilla = self.scintilla
		stringVal = scintilla.GetTextRange(start, end)
		if start > 0:
			stylenum = scintilla.SCIGetStyleAt(start - 1)
			styleStart = self.GetStyleByNum(stylenum).name
		else:
			styleStart = None
#		trace("Coloring", start, end, end-start, len(stringVal), styleStart, self.scintilla.SCIGetCharAt(start))
		scintilla.SCIStartStyling(start, 31)
		self.style_buffer = array.array("c", chr(0)*len(stringVal))
		self.ColorizeString(stringVal, styleStart)
		scintilla.SCISetStylingEx(self.style_buffer)
		self.style_buffer = None
#		trace("After styling, end styled is", self.scintilla.SCIGetEndStyled())
		if self.bCompleteWhileIdle and not self.bHaveIdleHandler and end!=-1 and end < scintilla.GetTextLength():
			self.bHaveIdleHandler = 1
			win32ui.GetApp().AddIdleHandler(self.DoMoreColoring)
			# Kicking idle makes the app seem slower when initially repainting!
#			win32ui.GetMainFrame().PostMessage(WM_KICKIDLE, 0, 0)

	def DoMoreColoring(self, handler, count):
		try:
			scintilla = self.scintilla
			endStyled = scintilla.SCIGetEndStyled()
			lineStartStyled = scintilla.LineFromChar(endStyled)
			start = scintilla.LineIndex(lineStartStyled)
			end = scintilla.LineIndex(lineStartStyled+1)
			textlen = scintilla.GetTextLength()
			if end < 0: end = textlen

			finished = end >= textlen
			self.Colorize(start, end)
		except (win32ui.error, AttributeError):
			# Window may have closed before we finished - no big deal!
			finished = 1

		if finished:
			self.bHaveIdleHandler = 0
			win32ui.GetApp().DeleteIdleHandler(handler)
		return not finished

# A Formatter that knows how to format Python source
from keyword import iskeyword, kwlist

wordstarts = '_0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
wordchars = '._0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
operators = '%^&*()-+=|{}[]:;<>,/?!.~'

STYLE_DEFAULT = "Whitespace"
STYLE_COMMENT = "Comment"
STYLE_COMMENT_BLOCK = "Comment Blocks"
STYLE_NUMBER = "Number"
STYLE_STRING = "String"
STYLE_SQSTRING = "SQ String"
STYLE_TQSSTRING = "TQS String"
STYLE_TQDSTRING = "TQD String"
STYLE_KEYWORD = "Keyword"
STYLE_CLASS = "Class"
STYLE_METHOD = "Method"
STYLE_OPERATOR = "Operator"
STYLE_IDENTIFIER = "Identifier"
STYLE_BRACE = "Brace/Paren - matching"
STYLE_BRACEBAD = "Brace/Paren - unmatched"
STYLE_STRINGEOL = "String with no terminator"

STRING_STYLES = [STYLE_STRING, STYLE_SQSTRING, STYLE_TQSSTRING, STYLE_TQDSTRING, STYLE_STRINGEOL]

# These styles can have any ID - they are not special to scintilla itself.
# However, if we use the built-in lexer, then we must use its style numbers
# so in that case, they _are_ special.
PYTHON_STYLES = [
		(STYLE_DEFAULT,      (0, 0, 200, 0, 0x808080), None,     scintillacon.SCE_P_DEFAULT ),
		(STYLE_COMMENT,      (0, 2, 200, 0, 0x008000), None,     scintillacon.SCE_P_COMMENTLINE ),
		(STYLE_COMMENT_BLOCK,(0, 2, 200, 0, 0x808080), None,     scintillacon.SCE_P_COMMENTBLOCK ),
		(STYLE_NUMBER,       (0, 0, 200, 0, 0x808000), None,     scintillacon.SCE_P_NUMBER ),
		(STYLE_STRING,       (0, 0, 200, 0, 0x008080), None,     scintillacon.SCE_P_STRING ),
		(STYLE_SQSTRING,     STYLE_STRING,             None,     scintillacon.SCE_P_CHARACTER ),
		(STYLE_TQSSTRING,    STYLE_STRING,             None,     scintillacon.SCE_P_TRIPLE ),
		(STYLE_TQDSTRING,    STYLE_STRING,             None,     scintillacon.SCE_P_TRIPLEDOUBLE),
		(STYLE_STRINGEOL,    (0, 0, 200, 0, 0x000000), 0x008080, scintillacon.SCE_P_STRINGEOL),
		(STYLE_KEYWORD,      (0, 1, 200, 0, 0x800000), None,     scintillacon.SCE_P_WORD),
		(STYLE_CLASS,        (0, 1, 200, 0, 0xFF0000), None,     scintillacon.SCE_P_CLASSNAME ),
		(STYLE_METHOD,       (0, 1, 200, 0, 0x808000), None,     scintillacon.SCE_P_DEFNAME),
		(STYLE_OPERATOR,     (0, 0, 200, 0, 0x000000), None,     scintillacon.SCE_P_OPERATOR),
		(STYLE_IDENTIFIER,   (0, 0, 200, 0, 0x000000), None,     scintillacon.SCE_P_IDENTIFIER ),
]

# These styles _always_ have this specific style number, regardless of
# internal or external formatter.
SPECIAL_STYLES = [
		(STYLE_BRACE,        (0, 0, 200, 0, 0x000000), 0xffff80, scintillacon.STYLE_BRACELIGHT),
		(STYLE_BRACEBAD,     (0, 0, 200, 0, 0x000000), 0x8ea5f2, scintillacon.STYLE_BRACEBAD),
]

PythonSampleCode = """\
# Some Python
class Sample(Super):
  def Fn(self):
\tself.v = 1024
dest = 'dest.html'
x = func(a + 1)|)
s = "I forget...
## A large
## comment block"""

class PythonSourceFormatter(Formatter):
	string_style_names = STRING_STYLES
	def GetSampleText(self):
		return PythonSampleCode

	def LoadStyles(self):
		pass

	def SetStyles(self):
		for name, format, bg, ignore in PYTHON_STYLES:
			self.RegisterStyle( Style(name, format, bg) )
		for name, format, bg, sc_id in SPECIAL_STYLES:
			self.RegisterStyle( Style(name, format, bg), sc_id )

	def ClassifyWord(self, cdoc, start, end, prevWord):
		word = cdoc[start:end+1]
		attr = STYLE_IDENTIFIER
		if prevWord == "class":
			attr = STYLE_CLASS
		elif prevWord == "def":
			attr = STYLE_METHOD
		elif cdoc[start] in string.digits:
			attr = STYLE_NUMBER
		elif iskeyword(word):
			attr = STYLE_KEYWORD
		self.ColorSeg(start, end, attr)
		return word

	def ColorizeString(self, str, styleStart):
		if styleStart is None: styleStart = STYLE_DEFAULT
		return self.ColorizePythonCode(str, 0, styleStart)

	def ColorizePythonCode(self, cdoc, charStart, styleStart):
		# Straight translation of C++, should do better
		lengthDoc = len(cdoc)
		if lengthDoc <= charStart: return
		prevWord = ""
		state = styleStart
		chPrev = chPrev2 = chPrev3 = ' '
		chNext = cdoc[charStart]
		chNext2 = cdoc[charStart]
		startSeg = i = charStart
		while i < lengthDoc:
			ch = chNext
			chNext = ' '
			if i+1 < lengthDoc: chNext = cdoc[i+1]
			chNext2 = ' '
			if i+2 < lengthDoc: chNext2 = cdoc[i+2]
			if state == STYLE_DEFAULT:
				if ch in wordstarts:
					self.ColorSeg(startSeg, i - 1, STYLE_DEFAULT)
					state = STYLE_KEYWORD
					startSeg = i
				elif ch == '#':
					self.ColorSeg(startSeg, i - 1, STYLE_DEFAULT)
					if chNext == '#':
						state = STYLE_COMMENT_BLOCK
					else:
						state = STYLE_COMMENT
					startSeg = i
				elif ch == '\"':
					self.ColorSeg(startSeg, i - 1, STYLE_DEFAULT)
					startSeg = i
					state = STYLE_COMMENT
					if chNext == '\"' and chNext2 == '\"':
						i = i + 2
						state = STYLE_TQDSTRING
						ch = ' '
						chPrev = ' '
						chNext = ' '
						if i+1 < lengthDoc: chNext = cdoc[i+1]
					else:
						state = STYLE_STRING
				elif ch == '\'':
					self.ColorSeg(startSeg, i - 1, STYLE_DEFAULT)
					startSeg = i
					state = STYLE_COMMENT
					if chNext == '\'' and chNext2 == '\'':
						i = i + 2
						state = STYLE_TQSSTRING
						ch = ' '
						chPrev = ' '
						chNext = ' '
						if i+1 < lengthDoc: chNext = cdoc[i+1]
					else:
						state = STYLE_SQSTRING
				elif ch in operators:
					self.ColorSeg(startSeg, i - 1, STYLE_DEFAULT)
					self.ColorSeg(i, i, STYLE_OPERATOR)
					startSeg = i+1
			elif state == STYLE_KEYWORD:
				if ch not in wordchars:
					prevWord = self.ClassifyWord(cdoc, startSeg, i-1, prevWord)
					state = STYLE_DEFAULT
					startSeg = i
					if ch == '#':
						if chNext == '#':
							state = STYLE_COMMENT_BLOCK
						else:
							state = STYLE_COMMENT
					elif ch == '\"':
						if chNext == '\"' and chNext2 == '\"':
							i = i + 2
							state = STYLE_TQDSTRING
							ch = ' '
							chPrev = ' '
							chNext = ' '
							if i+1 < lengthDoc: chNext = cdoc[i+1]
						else:
							state = STYLE_STRING
					elif ch == '\'':
						if chNext == '\'' and chNext2 == '\'':
							i = i + 2
							state = STYLE_TQSSTRING
							ch = ' '
							chPrev = ' '
							chNext = ' '
							if i+1 < lengthDoc: chNext = cdoc[i+1]
						else:
							state = STYLE_SQSTRING
					elif ch in operators:
						self.ColorSeg(startSeg, i, STYLE_OPERATOR)
						startSeg = i+1
			elif state == STYLE_COMMENT or state == STYLE_COMMENT_BLOCK:
				if ch == '\r' or ch == '\n':
					self.ColorSeg(startSeg, i-1, state)
					state = STYLE_DEFAULT
					startSeg = i
			elif state == STYLE_STRING:
				if ch == '\\':
					if chNext == '\"' or chNext == '\'' or chNext == '\\':
						i = i + 1
						ch = chNext
						chNext = ' '
						if i+1 < lengthDoc: chNext = cdoc[i+1]
				elif ch == '\"':
					self.ColorSeg(startSeg, i, STYLE_STRING)
					state = STYLE_DEFAULT
					startSeg = i+1
			elif state == STYLE_SQSTRING:
				if ch == '\\':
					if chNext == '\"' or chNext == '\'' or chNext == '\\':
						i = i+1
						ch = chNext
						chNext = ' '
						if i+1 < lengthDoc: chNext = cdoc[i+1]
				elif ch == '\'':
					self.ColorSeg(startSeg, i, STYLE_SQSTRING)
					state = STYLE_DEFAULT
					startSeg = i+1
			elif state == STYLE_TQSSTRING:
				if ch == '\'' and chPrev == '\'' and chPrev2 == '\'' and chPrev3 != '\\':
					self.ColorSeg(startSeg, i, STYLE_TQSSTRING)
					state = STYLE_DEFAULT
					startSeg = i+1
			elif state == STYLE_TQDSTRING and ch == '\"' and chPrev == '\"' and chPrev2 == '\"' and chPrev3 != '\\':
					self.ColorSeg(startSeg, i, STYLE_TQDSTRING)
					state = STYLE_DEFAULT
					startSeg = i+1
			chPrev3 = chPrev2
			chPrev2 = chPrev
			chPrev = ch
			i = i + 1
		if startSeg < lengthDoc:
			if state == STYLE_KEYWORD:
				self.ClassifyWord(cdoc, startSeg, lengthDoc-1, prevWord)
			else:
				self.ColorSeg(startSeg, lengthDoc-1, state)


# These taken from the SciTE properties file.
source_formatter_extensions = [
	( string.split(".py .pys .pyw"), scintillacon.SCLEX_PYTHON ),
	( string.split(".html .htm .asp .shtml"), scintillacon.SCLEX_HTML ),
	( string.split("c .cc .cpp .cxx .h .hh .hpp .hxx .idl .odl .php3 .phtml .inc .js"),scintillacon.SCLEX_CPP ),
	( string.split(".vbs .frm .ctl .cls"), scintillacon.SCLEX_VB ),
	( string.split(".pl .pm .cgi .pod"), scintillacon.SCLEX_PERL ),
	( string.split(".sql .spec .body .sps .spb .sf .sp"), scintillacon.SCLEX_SQL ),
	( string.split(".tex .sty"), scintillacon.SCLEX_LATEX ),
	( string.split(".xml .xul"), scintillacon.SCLEX_XML ),
	( string.split(".err"), scintillacon.SCLEX_ERRORLIST ),
	( string.split(".mak"), scintillacon.SCLEX_MAKEFILE ),
	( string.split(".bat .cmd"), scintillacon.SCLEX_BATCH ),
]

class BuiltinSourceFormatter(FormatterBase):
	# A class that represents a formatter built-in to Scintilla
	def __init__(self, scintilla, ext):
		self.ext = ext
		FormatterBase.__init__(self, scintilla)

	def Colorize(self, start=0, end=-1):
		self.scintilla.SendScintilla(scintillacon.SCI_COLOURISE, start, end)
	def RegisterStyle(self, style, stylenum = None):
		assert style.stylenum is None, "Style has already been registered"
		if stylenum is None:
			stylenum = self.nextstylenum
			self.nextstylenum = self.nextstylenum + 1
		assert self.styles.get(stylenum) is None, "We are reusing a style number!"
		style.stylenum = stylenum
		self.styles[style.name] = style
		self.styles_by_id[stylenum] = style

	def HookFormatter(self, parent = None):
		sc = self.scintilla
		for exts, formatter in source_formatter_extensions:
			if self.ext in exts:
				formatter_use = formatter
				break
		else:
			formatter_use = scintillacon.SCLEX_PYTHON
		sc.SendScintilla(scintillacon.SCI_SETLEXER, formatter_use)
		keywords = string.join(kwlist)
		sc.SCISetKeywords(keywords)

class BuiltinPythonSourceFormatter(BuiltinSourceFormatter):
	sci_lexer_name = scintillacon.SCLEX_PYTHON
	string_style_names = STRING_STYLES
	def __init__(self, sc, ext = ".py"):
		BuiltinSourceFormatter.__init__(self, sc, ext)
	def SetStyles(self):
		for name, format, bg, sc_id in PYTHON_STYLES:
			self.RegisterStyle( Style(name, format, bg), sc_id )
		for name, format, bg, sc_id in SPECIAL_STYLES:
			self.RegisterStyle( Style(name, format, bg), sc_id )
	def GetSampleText(self):
		return PythonSampleCode
