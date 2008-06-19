import string
import win32con

char_ranges = [
    (string.lowercase, -32),
    (string.digits, 0),
    ("?><:[]\\", 128),
    (";", 127),
    ("=", 126),
    ("/.,", 144),
    ("`{}|", 96),
    ("_", 94),
    ("-+", 144),
    ("'", 183),
    ('"', 188),
    ("~", 66),
    ("!",16),
    ("#$%&", 18),
    ("()", 17),
]

key_name_to_code = {}
key_code_to_name = {}

_better_names = [
    ("esc", win32con.VK_ESCAPE),
    ("enter", win32con.VK_RETURN),
    ("pgup", win32con.VK_BACK),
    ("pgdn", win32con.VK_NEXT),
]
def _fillmap():
    # Pull the VK_names from win32con
    names = filter(lambda entry: entry[:3]=="VK_", win32con.__dict__.keys())
    for name in names:
        n = string.lower(name[3:])
        val = getattr(win32con, name)
        key_name_to_code[n] = val
        key_code_to_name[val] = n
    # Some better named we know about
    for name, code in _better_names:
        key_name_to_code[name] = code
        key_code_to_name[code] = name
    # And the char_ranges map above
    for chars, offset in char_ranges:
        for char in chars:
            key_name_to_code[char] = ord(char)+offset
            key_code_to_name[ord(char)+offset] = char

_fillmap()

def get_scan_code(chardesc):
    return key_name_to_code.get(string.lower(chardesc))

modifiers = {
    "alt" : win32con.LEFT_ALT_PRESSED | win32con.RIGHT_ALT_PRESSED, 
    "lalt" : win32con.LEFT_ALT_PRESSED, 
    "ralt" : win32con.RIGHT_ALT_PRESSED,
    "ctrl" : win32con.LEFT_CTRL_PRESSED | win32con.RIGHT_CTRL_PRESSED,
    "ctl" : win32con.LEFT_CTRL_PRESSED | win32con.RIGHT_CTRL_PRESSED,
    "control" : win32con.LEFT_CTRL_PRESSED | win32con.RIGHT_CTRL_PRESSED,
    "lctrl" : win32con.LEFT_CTRL_PRESSED,
    "lctl" : win32con.LEFT_CTRL_PRESSED,
    "rctrl" : win32con.RIGHT_CTRL_PRESSED,
    "rctl" : win32con.RIGHT_CTRL_PRESSED,
    "shift" : win32con.SHIFT_PRESSED,
    "key" : 0, # ignore key tag.
}

def parse_key_name(name):
    name = name + "-" # Add a sentinal
    start = pos = 0
    max = len(name)
    flags = 0
    scancode = None
    while pos<max:
        if name[pos] in "+-":
            tok = string.lower(name[start:pos])
            mod = modifiers.get(tok)
            if mod is None:
                # Its a key name
                scancode = get_scan_code(tok)
            else:
                flags = flags | mod
            pos = pos + 1 # skip the sep
            start = pos
        pos = pos + 1
    return scancode, flags

_checks = [
    [ # Shift
    ("Shift", win32con.SHIFT_PRESSED),
    ],
    [ # Ctrl key
    ("Ctrl", win32con.LEFT_CTRL_PRESSED | win32con.RIGHT_CTRL_PRESSED),
    ("LCtrl", win32con.LEFT_CTRL_PRESSED),
    ("RCtrl", win32con.RIGHT_CTRL_PRESSED),
    ],
    [ # Alt key
    ("Alt", win32con.LEFT_ALT_PRESSED | win32con.RIGHT_ALT_PRESSED),
    ("LAlt", win32con.LEFT_ALT_PRESSED),
    ("RAlt", win32con.RIGHT_ALT_PRESSED),
    ],
]

def make_key_name(scancode, flags):
    # Check alt keys.
    flags_done = 0
    parts = []
    for moddata in _checks:
        for name, checkflag in moddata:
            if flags & checkflag:
                parts.append(name)
                flags_done = flags_done & checkflag
                break
    if flags_done & flags:
        parts.append(hex( flags & ~flags_done ) )
    # Now the key name.
    try:
        parts.append(key_code_to_name[scancode])
    except KeyError:
        parts.append( "<Unknown scan code %s>" % scancode )
    sep = "+"
    if sep in parts: sep = "-"
    return string.join(map(string.capitalize, parts), sep)

def _psc(char):
    sc = get_scan_code(char)
    print "Char %s -> %d -> %s" % (`char`, sc, key_code_to_name.get(sc))

def test1():
    for ch in """aA0/?[{}];:'"`~_-+=\\|,<.>/?""":
        _psc(ch)
    for code in ["Home", "End", "Left", "Right", "Up", "Down", "Menu", "Next"]:
        _psc(code)

def _pkn(n):
    scancode, flags = parse_key_name(n)
    print "%s -> %s,%s -> %s" % (n, scancode, flags, make_key_name(scancode, flags))

def test2():
    _pkn("ctrl+alt-shift+x")
    _pkn("ctrl-home")
    _pkn("Shift-+")
    _pkn("Shift--")
    _pkn("Shift+-")
    _pkn("Shift++")
    _pkn("LShift-+")
    _pkn("ctl+home")
    _pkn("ctl+enter")
    _pkn("alt+return")
    _pkn("Alt+/")
    _pkn("Alt+BadKeyName")
    _pkn("A")
    _pkn("(")
    _pkn("Ctrl+(")
    _pkn("{")
    _pkn("!")

if __name__=='__main__':
    test2()