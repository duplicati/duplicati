# This is a work in progress - see Demos/win32gui_menu.py

# win32gui_struct.py - helpers for working with various win32gui structures.
# As win32gui is "light-weight", it does not define objects for all possible
# win32 structures - in general, "buffer" objects are passed around - it is
# the callers responsibility to pack the buffer in the correct format.
#
# This module defines some helpers for the commonly used structures.
#
# In general, each structure has 3 functions:
#
# buffer, extras = PackSTRUCTURE(items, ...)
# item, ... = UnpackSTRUCTURE(buffer)
# buffer, extras = EmtpySTRUCTURE(...)
#
# 'extras' is always items that must be held along with the buffer, as the
# buffer refers to these object's memory.
# For structures that support a 'mask', this mask is hidden from the user - if
# 'None' is passed, the mask flag will not be set, or on return, None will
# be returned for the value if the mask is not set.
#
# NOTE: I considered making these structures look like real classes, and
# support 'attributes' etc - however, ctypes already has a good structure
# mechanism - I think it makes more sense to support ctype structures
# at the win32gui level, then there will be no need for this module at all.

import win32gui
import win32con
import struct
import array
import commctrl

# Generic WM_NOTIFY unpacking
def UnpackWMNOTIFY(lparam):
    format = "iii"
    buf = win32gui.PyMakeBuffer(struct.calcsize(format), lparam)
    hwndFrom, idFrom, code = struct.unpack(format, buf)
    return hwndFrom, idFrom, code
    
# MENUITEMINFO struct
# http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/WinUI/WindowsUserInterface/Resources/Menus/MenuReference/MenuStructures/MENUITEMINFO.asp
# We use the struct module to pack and unpack strings as MENUITEMINFO
# structures.  We also have special handling for the 'fMask' item in that
# structure to avoid the caller needing to explicitly check validity
# (None is used if the mask excludes/should exclude the value)
menuitem_fmt = '9iP2i'

def PackMENUITEMINFO(fType=None, fState=None, wID=None, hSubMenu=None,
                     hbmpChecked=None, hbmpUnchecked=None, dwItemData=None,
                     text=None, hbmpItem=None, dwTypeData=None):
    # 'extras' are objects the caller must keep a reference to (as their
    # memory is used) for the lifetime of the INFO item.
    extras = []
    # ack - dwItemData and dwTypeData were confused for a while...
    assert dwItemData is None or dwTypeData is None, \
           "sorry - these were confused - you probably want dwItemData"
    # if we are a long way past 209, then we can nuke the above...
    if dwTypeData is not None:
        import warnings
        warnings.warn("PackMENUITEMINFO: please use dwItemData instead of dwTypeData")
    if dwItemData is None:
        dwItemData = dwTypeData or 0

    fMask = 0
    if fType is None: fType = 0
    else: fMask |= win32con.MIIM_FTYPE
    if fState is None: fState = 0
    else: fMask |= win32con.MIIM_STATE
    if wID is None: wID = 0
    else: fMask |= win32con.MIIM_ID
    if hSubMenu is None: hSubMenu = 0
    else: fMask |= win32con.MIIM_SUBMENU
    if hbmpChecked is None:
        assert hbmpUnchecked is None, \
                "neither or both checkmark bmps must be given"
        hbmpChecked = hbmpUnchecked = 0
    else:
        assert hbmpUnchecked is not None, \
                "neither or both checkmark bmps must be given"
        fMask |= win32con.MIIM_CHECKMARKS
    if dwItemData is None: dwItemData = 0
    else: fMask |= win32con.MIIM_DATA
    if hbmpItem is None: hbmpItem = 0
    else: fMask |= win32con.MIIM_BITMAP
    if text is not None:
        fMask |= win32con.MIIM_STRING
        if isinstance(text, unicode):
            text = text.encode("mbcs")
        str_buf = array.array("c", text+'\0')
        cch = len(str_buf)
        # We are taking address of strbuf - it must not die until windows
        # has finished with our structure.
        lptext = str_buf.buffer_info()[0]
        extras.append(str_buf)
    else:
        lptext = 0
        cch = 0
    # Create the struct.
    item = struct.pack(
                menuitem_fmt,
                struct.calcsize(menuitem_fmt), # cbSize
                fMask,
                fType,
                fState,
                wID,
                hSubMenu,
                hbmpChecked,
                hbmpUnchecked,
                dwItemData,
                lptext,
                cch,
                hbmpItem
                )
    # Now copy the string to a writable buffer, so that the result
    # could be passed to a 'Get' function
    return array.array("c", item), extras

def UnpackMENUITEMINFO(s):
    (cb,
    fMask,
    fType,
    fState,
    wID,
    hSubMenu,
    hbmpChecked,
    hbmpUnchecked,
    dwItemData,
    lptext,
    cch,
    hbmpItem) = struct.unpack(menuitem_fmt, s)
    assert cb==len(s)
    if fMask & win32con.MIIM_FTYPE==0: fType = None
    if fMask & win32con.MIIM_STATE==0: fState = None
    if fMask & win32con.MIIM_ID==0: wID = None
    if fMask & win32con.MIIM_SUBMENU==0: hSubMenu = None
    if fMask & win32con.MIIM_CHECKMARKS==0: hbmpChecked = hbmpUnchecked = None
    if fMask & win32con.MIIM_DATA==0: dwItemData = None
    if fMask & win32con.MIIM_BITMAP==0: hbmpItem = None
    if fMask & win32con.MIIM_STRING:
        text = win32gui.PyGetString(lptext, cch)
    else:
        text = None
    return fType, fState, wID, hSubMenu, hbmpChecked, hbmpUnchecked, \
           dwItemData, text, hbmpItem

def EmptyMENUITEMINFO(mask = None, text_buf_size=512):
    extra = []
    if mask is None:
        mask = win32con.MIIM_BITMAP | win32con.MIIM_CHECKMARKS | \
               win32con.MIIM_DATA | win32con.MIIM_FTYPE | \
               win32con.MIIM_ID | win32con.MIIM_STATE | \
               win32con.MIIM_STRING | win32con.MIIM_SUBMENU
               # Note: No MIIM_TYPE - this screws win2k/98.
 
    if mask & win32con.MIIM_STRING:
        text_buffer = array.array("c", "\0" * text_buf_size)
        extra.append(text_buffer)
        text_addr, text_len = text_buffer.buffer_info()
    else:
        text_addr = text_len = 0

    buf = struct.pack(
                menuitem_fmt,
                struct.calcsize(menuitem_fmt), # cbSize
                mask,
                0, #fType,
                0, #fState,
                0, #wID,
                0, #hSubMenu,
                0, #hbmpChecked,
                0, #hbmpUnchecked,
                0, #dwItemData,
                text_addr,
                text_len,
                0, #hbmpItem
                )
    return array.array("c", buf), extra

# MENUINFO struct
menuinfo_fmt = '7i'

def PackMENUINFO(dwStyle = None, cyMax = None,
                 hbrBack = None, dwContextHelpID = None, dwMenuData = None,
                 fMask = 0):
    if dwStyle is None: dwStyle = 0
    else: fMask |= win32con.MIM_STYLE
    if cyMax is None: cyMax = 0
    else: fMask |= win32con.MIM_MAXHEIGHT
    if hbrBack is None: hbrBack = 0
    else: fMask |= win32con.MIM_BACKGROUND
    if dwContextHelpID is None: dwContextHelpID = 0
    else: fMask |= win32con.MIM_HELPID
    if dwMenuData is None: dwMenuData = 0
    else: fMask |= win32con.MIM_MENUDATA
    # Create the struct.
    item = struct.pack(
                menuinfo_fmt,
                struct.calcsize(menuinfo_fmt), # cbSize
                fMask,
                dwStyle,
                cyMax,
                hbrBack,
                dwContextHelpID,
                dwMenuData)
    return array.array("c", item)

def UnpackMENUINFO(s):
    (cb,
    fMask,
    dwStyle,
    cyMax,
    hbrBack,
    dwContextHelpID,
    dwMenuData) = struct.unpack(menuinfo_fmt, s)
    assert cb==len(s)
    if fMask & win32con.MIM_STYLE==0: dwStyle = None
    if fMask & win32con.MIM_MAXHEIGHT==0: cyMax = None
    if fMask & win32con.MIM_BACKGROUND==0: hbrBack = None
    if fMask & win32con.MIM_HELPID==0: dwContextHelpID = None
    if fMask & win32con.MIM_MENUDATA==0: dwMenuData = None
    return dwStyle, cyMax, hbrBack, dwContextHelpID, dwMenuData

def EmptyMENUINFO(mask = None):
    if mask is None:
        mask = win32con.MIM_STYLE | win32con.MIM_MAXHEIGHT| \
               win32con.MIM_BACKGROUND | win32con.MIM_HELPID | \
               win32con.MIM_MENUDATA
 
    buf = struct.pack(
                menuinfo_fmt,
                struct.calcsize(menuinfo_fmt), # cbSize
                mask,
                0, #dwStyle
                0, #cyMax
                0, #hbrBack,
                0, #dwContextHelpID,
                0, #dwMenuData,
                )
    return array.array("c", buf)

##########################################################################
#
# Tree View structure support - TVITEM, TVINSERTSTRUCT and TVDISPINFO
# 
##########################################################################

# XXX - Note that the following implementation of TreeView structures is ripped
# XXX - from the SpamBayes project.  It may not quite work correctly yet - I
# XXX - intend checking them later - but having them is better than not at all!

# Helpers for the ugly win32 structure packing/unpacking
# XXX - Note that functions using _GetMaskAndVal run 3x faster if they are
# 'inlined' into the function - see PackLVITEM.  If the profiler points at
# _GetMaskAndVal(), you should nuke it (patches welcome once they have been
# tested)
def _GetMaskAndVal(val, default, mask, flag):
    if val is None:
        return mask, default
    else:
        if flag is not None:
            mask |= flag
        return mask, val

def PackTVINSERTSTRUCT(parent, insertAfter, tvitem):
    tvitem_buf, extra = PackTVITEM(*tvitem)
    tvitem_buf = tvitem_buf.tostring()
    format = "ii%ds" % len(tvitem_buf)
    return struct.pack(format, parent, insertAfter, tvitem_buf), extra

def PackTVITEM(hitem, state, stateMask, text, image, selimage, citems, param):
    extra = [] # objects we must keep references to
    mask = 0
    mask, hitem = _GetMaskAndVal(hitem, 0, mask, commctrl.TVIF_HANDLE)
    mask, state = _GetMaskAndVal(state, 0, mask, commctrl.TVIF_STATE)
    if not mask & commctrl.TVIF_STATE:
        stateMask = 0
    mask, text = _GetMaskAndVal(text, None, mask, commctrl.TVIF_TEXT)
    mask, image = _GetMaskAndVal(image, 0, mask, commctrl.TVIF_IMAGE)
    mask, selimage = _GetMaskAndVal(selimage, 0, mask, commctrl.TVIF_SELECTEDIMAGE)
    mask, citems = _GetMaskAndVal(citems, 0, mask, commctrl.TVIF_CHILDREN)
    mask, param = _GetMaskAndVal(param, 0, mask, commctrl.TVIF_PARAM)
    if text is None:
        text_addr = text_len = 0
    else:
        if isinstance(text, unicode):
            text = text.encode("mbcs")
        text_buffer = array.array("c", text+"\0")
        extra.append(text_buffer)
        text_addr, text_len = text_buffer.buffer_info()
    format = "iiiiiiiiii"
    buf = struct.pack(format,
                      mask, hitem,
                      state, stateMask,
                      text_addr, text_len, # text
                      image, selimage,
                      citems, param)
    return array.array("c", buf), extra

# Make a new buffer suitable for querying hitem's attributes.
def EmptyTVITEM(hitem, mask = None, text_buf_size=512):
    extra = [] # objects we must keep references to
    if mask is None:
        mask = commctrl.TVIF_HANDLE | commctrl.TVIF_STATE | commctrl.TVIF_TEXT | \
               commctrl.TVIF_IMAGE | commctrl.TVIF_SELECTEDIMAGE | \
               commctrl.TVIF_CHILDREN | commctrl.TVIF_PARAM
    if mask & commctrl.TVIF_TEXT:
        text_buffer = array.array("c", "\0" * text_buf_size)
        extra.append(text_buffer)
        text_addr, text_len = text_buffer.buffer_info()
    else:
        text_addr = text_len = 0
    format = "iiiiiiiiii"
    buf = struct.pack(format,
                      mask, hitem,
                      0, 0,
                      text_addr, text_len, # text
                      0, 0,
                      0, 0)
    return array.array("c", buf), extra
    
def UnpackTVITEM(buffer):
    item_mask, item_hItem, item_state, item_stateMask, \
        item_textptr, item_cchText, item_image, item_selimage, \
        item_cChildren, item_param = struct.unpack("10i", buffer)
    # ensure only items listed by the mask are valid (except we assume the
    # handle is always valid - some notifications (eg, TVN_ENDLABELEDIT) set a
    # mask that doesn't include the handle, but the docs explicity say it is.)
    if not (item_mask & commctrl.TVIF_TEXT): item_textptr = item_cchText = None
    if not (item_mask & commctrl.TVIF_CHILDREN): item_cChildren = None
    if not (item_mask & commctrl.TVIF_IMAGE): item_image = None
    if not (item_mask & commctrl.TVIF_PARAM): item_param = None
    if not (item_mask & commctrl.TVIF_SELECTEDIMAGE): item_selimage = None
    if not (item_mask & commctrl.TVIF_STATE): item_state = item_stateMask = None
    
    if item_textptr:
        text = win32gui.PyGetString(item_textptr)
    else:
        text = None
    return item_hItem, item_state, item_stateMask, \
        text, item_image, item_selimage, \
        item_cChildren, item_param

# Unpack the lparm from a "TVNOTIFY" message
def UnpackTVNOTIFY(lparam):
    format = "iiii40s40s"
    buf = win32gui.PyMakeBuffer(struct.calcsize(format), lparam)
    hwndFrom, id, code, action, buf_old, buf_new \
          = struct.unpack(format, buf)
    item_old = UnpackTVITEM(buf_old)
    item_new = UnpackTVITEM(buf_new)
    return hwndFrom, id, code, action, item_old, item_new

def UnpackTVDISPINFO(lparam):
    format = "iii40s"
    buf = win32gui.PyMakeBuffer(struct.calcsize(format), lparam)
    hwndFrom, id, code, buf_item = struct.unpack(format, buf)
    item = UnpackTVITEM(buf_item)
    return hwndFrom, id, code, item

#
# List view items
def PackLVITEM(item=None, subItem=None, state=None, stateMask=None, text=None, image=None, param=None, indent=None):
    extra = [] # objects we must keep references to
    mask = 0
    # _GetMaskAndVal adds quite a bit of overhead to this function.
    if item is None: item = 0 # No mask for item
    if subItem is None: subItem = 0 # No mask for sibItem
    if state is None:
        state = 0
        stateMask = 0
    else:
        mask |= commctrl.LVIF_STATE
        if stateMask is None: stateMask = state
    
    if image is None: image = 0
    else: mask |= commctrl.LVIF_IMAGE
    if param is None: param = 0
    else: mask |= commctrl.LVIF_PARAM
    if indent is None: indent = 0
    else: mask |= commctrl.LVIF_INDENT

    if text is None:
        text_addr = text_len = 0
    else:
        mask |= commctrl.LVIF_TEXT
        if isinstance(text, unicode):
            text = text.encode("mbcs")
        text_buffer = array.array("c", text+"\0")
        extra.append(text_buffer)
        text_addr, text_len = text_buffer.buffer_info()
    format = "iiiiiiiiii"
    buf = struct.pack(format,
                      mask, item, subItem,
                      state, stateMask,
                      text_addr, text_len, # text
                      image, param, indent)
    return array.array("c", buf), extra

def UnpackLVITEM(buffer):
    item_mask, item_item, item_subItem, \
        item_state, item_stateMask, \
        item_textptr, item_cchText, item_image, \
        item_param, item_indent = struct.unpack("10i", buffer)
    # ensure only items listed by the mask are valid
    if not (item_mask & commctrl.LVIF_TEXT): item_textptr = item_cchText = None
    if not (item_mask & commctrl.LVIF_IMAGE): item_image = None
    if not (item_mask & commctrl.LVIF_PARAM): item_param = None
    if not (item_mask & commctrl.LVIF_INDENT): item_indent = None
    if not (item_mask & commctrl.LVIF_STATE): item_state = item_stateMask = None
    
    if item_textptr:
        text = win32gui.PyGetString(item_textptr)
    else:
        text = None
    return item_item, item_subItem, item_state, item_stateMask, \
        text, item_image, item_param, item_indent

# Unpack an "LVNOTIFY" message
def UnpackLVDISPINFO(lparam):
    format = "iii40s"
    buf = win32gui.PyMakeBuffer(struct.calcsize(format), lparam)
    hwndFrom, id, code, buf_item = struct.unpack(format, buf)
    item = UnpackLVITEM(buf_item)
    return hwndFrom, id, code, item

def UnpackLVNOTIFY(lparam):
    format = "3i8i"
    buf = win32gui.PyMakeBuffer(struct.calcsize(format), lparam)
    hwndFrom, id, code, item, subitem, newstate, oldstate, \
        changed, pt_x, pt_y, lparam = struct.unpack(format, buf)
    return hwndFrom, id, code, item, subitem, newstate, oldstate, \
        changed, (pt_x, pt_y), lparam


# Make a new buffer suitable for querying an items attributes.
def EmptyLVITEM(item, subitem, mask = None, text_buf_size=512):
    extra = [] # objects we must keep references to
    if mask is None:
        mask = commctrl.LVIF_IMAGE | commctrl.LVIF_INDENT | commctrl.LVIF_TEXT | \
               commctrl.LVIF_PARAM | commctrl.LVIF_STATE
    if mask & commctrl.LVIF_TEXT:
        text_buffer = array.array("c", "\0" * text_buf_size)
        extra.append(text_buffer)
        text_addr, text_len = text_buffer.buffer_info()
    else:
        text_addr = text_len = 0
    format = "iiiiiiiiii"
    buf = struct.pack(format,
                      mask, item, subitem, 
                      0, 0,
                      text_addr, text_len, # text
                      0, 0, 0)
    return array.array("c", buf), extra


# List view column structure
def PackLVCOLUMN(fmt=None, cx=None, text=None, subItem=None, image=None, order=None):
    extra = [] # objects we must keep references to
    mask = 0
    mask, fmt = _GetMaskAndVal(fmt, 0, mask, commctrl.LVCF_FMT)
    mask, cx = _GetMaskAndVal(cx, 0, mask, commctrl.LVCF_WIDTH)
    mask, text = _GetMaskAndVal(text, None, mask, commctrl.LVCF_TEXT)
    mask, subItem = _GetMaskAndVal(subItem, 0, mask, commctrl.LVCF_SUBITEM)
    mask, image = _GetMaskAndVal(image, 0, mask, commctrl.LVCF_IMAGE)
    mask, order= _GetMaskAndVal(order, 0, mask, commctrl.LVCF_ORDER)
    if text is None:
        text_addr = text_len = 0
    else:
        if isinstance(text, unicode):
            text = text.encode("mbcs")
        text_buffer = array.array("c", text+"\0")
        extra.append(text_buffer)
        text_addr, text_len = text_buffer.buffer_info()
    format = "iiiiiiii"
    buf = struct.pack(format,
                      mask, fmt, cx,
                      text_addr, text_len, # text
                      subItem, image, order)
    return array.array("c", buf), extra

def UnpackLVCOLUMN(lparam):
    format = "iiiiiiii"
    mask, fmt, cx, text_addr, text_size, subItem, image, order = \
            struct.unpack(format, lparam)
    # ensure only items listed by the mask are valid
    if not (mask & commctrl.LVCF_FMT): fmt = None
    if not (mask & commctrl.LVCF_WIDTH): cx = None
    if not (mask & commctrl.LVCF_TEXT): text_addr = text_size = None
    if not (mask & commctrl.LVCF_SUBITEM): subItem = None
    if not (mask & commctrl.LVCF_IMAGE): image = None
    if not (mask & commctrl.LVCF_ORDER): order = None
    if text_addr:
        text = win32gui.PyGetString(text_addr)
    else:
        text = None
    return fmt, cx, text, subItem, image, order


# Make a new buffer suitable for querying an items attributes.
def EmptyLVCOLUMN(mask = None, text_buf_size=512):
    extra = [] # objects we must keep references to
    if mask is None:
        mask = commctrl.LVCF_FMT | commctrl.LVCF_WIDTH | commctrl.LVCF_TEXT | \
               commctrl.LVCF_SUBITEM | commctrl.LVCF_IMAGE | commctrl.LVCF_ORDER
    if mask & commctrl.LVCF_TEXT:
        text_buffer = array.array("c", "\0" * text_buf_size)
        extra.append(text_buffer)
        text_addr, text_len = text_buffer.buffer_info()
    else:
        text_addr = text_len = 0
    format = "iiiiiiii"
    buf = struct.pack(format,
                      mask, 0, 0,
                      text_addr, text_len, # text
                      0, 0, 0)
    return array.array("c", buf), extra

# List view hit-test.
def PackLVHITTEST(pt):
    format = "iiiii"
    buf = struct.pack(format,
                      pt[0], pt[1],
                      0, 0, 0)
    return array.array("c", buf), None

def UnpackLVHITTEST(buf):
    format = "iiiii"
    x, y, flags, item, subitem = struct.unpack(format, buf)
    return (x,y), flags, item, subitem

def PackHDITEM(cxy = None, text = None, hbm = None, fmt = None,
               param = None, image = None, order = None):
    extra = [] # objects we must keep references to
    mask = 0
    mask, cxy = _GetMaskAndVal(cxy, 0, mask, commctrl.HDI_HEIGHT)
    mask, text = _GetMaskAndVal(text, None, mask, commctrl.LVCF_TEXT)
    mask, hbm = _GetMaskAndVal(hbm, 0, mask, commctrl.HDI_BITMAP)
    mask, fmt = _GetMaskAndVal(fmt, 0, mask, commctrl.HDI_FORMAT)
    mask, param = _GetMaskAndVal(param, 0, mask, commctrl.HDI_LPARAM)
    mask, image = _GetMaskAndVal(image, 0, mask, commctrl.HDI_IMAGE)
    mask, order = _GetMaskAndVal(order, 0, mask, commctrl.HDI_ORDER)

    if text is None:
        text_addr = text_len = 0
    else:
        if isinstance(text, unicode):
            text = text.encode("mbcs")
        text_buffer = array.array("c", text+"\0")
        extra.append(text_buffer)
        text_addr, text_len = text_buffer.buffer_info()

    format = "iiiiiiiiiii"
    buf = struct.pack(format,
                      mask, cxy, text_addr, hbm, text_len,
                      fmt, param, image, order, 0, 0)
    return array.array("c", buf), extra
