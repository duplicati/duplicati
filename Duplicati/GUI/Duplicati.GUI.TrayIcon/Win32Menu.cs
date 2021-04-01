//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Runtime.ConstrainedExecution;
using System.IO;

namespace Duplicati.GUI.TrayIcon
{
    public class SafeObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        private static extern bool DeleteObject(IntPtr hObject);
        public SafeObjectHandle(IntPtr hObject) : base(true)
        {
            SetHandle(hObject);
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        protected override bool ReleaseHandle()
        {
            return DeleteObject(handle);
        }
    }

    public class SafeMenuHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        private static extern bool DestroyMenu(IntPtr hMenu);
        public SafeMenuHandle(IntPtr hMenu) : base(true)
        {
            SetHandle(hMenu);
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        protected override bool ReleaseHandle()
        {
            return DestroyMenu(handle);
        }
    }

    public static class Win32NativeMenu
    {
        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(SafeMenuHandle hMenu, TPM wFlags, int x, int y, IntPtr hWnd, IntPtr tpmParams);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool AppendMenu(SafeMenuHandle hMenu, MenuFlags uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool EnableMenuItem(SafeMenuHandle hMenu, uint uIDEnableItem, MenuFlags uEnable);

        [DllImport("user32.dll")]
        private static extern bool SetMenuItemBitmaps(SafeMenuHandle hMenu, uint uPosition, MFT uFlags, IntPtr hBitmapUnchecked, IntPtr hBitmapChecked);

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(SafeIconHandle hIcon, out ICONINFO piconinfo);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MENUITEMINFO
        {
            public Int32 cbSize = Marshal.SizeOf(typeof(MENUITEMINFO));
            public MIIM fMask;
            public MFT fType;
            public MFS fState;
            public UInt32 wID;
            public IntPtr hSubMenu;
            public IntPtr hbmpChecked;
            public IntPtr hbmpUnchecked;
            public IntPtr dwItemData;
            public string dwTypeData = null;
            public UInt32 cch; // length of dwTypeData
            public IntPtr hbmpItem;

            public MENUITEMINFO() { }
            public MENUITEMINFO(MIIM pfMask)
            {
                fMask = pfMask;
            }
        }

        [Flags]
        private enum MIIM
        {
            BITMAP = 0x00000080,
            CHECKMARKS = 0x00000008,
            DATA = 0x00000020,
            FTYPE = 0x00000100,
            ID = 0x00000002,
            STATE = 0x00000001,
            STRING = 0x00000040,
            SUBMENU = 0x00000004,
            TYPE = 0x00000010
        }

        [Flags]
        public enum TPM : uint
        {
            TPM_LEFTBUTTON = 0x0000,
            TPM_RIGHTBUTTON = 0x0002,
            TPM_LEFTALIGN = 0x0000,
            TPM_CENTERALIGN = 0x0004,
            TPM_RIGHTALIGN = 0x0008,
            TPM_TOPALIGN = 0x0000,
            TPM_VCENTERALIGN = 0x0010,
            TPM_BOTTOMALIGN = 0x0020,
            TPM_HORIZONTAL = 0x0000,
            TPM_VERTICAL = 0x0040,
            TPM_NONOTIFY = 0x0080,
            TPM_RETURNCMD = 0x0100,
            TPM_RECURSE = 0x0001,
            TPM_HORPOSANIMATION = 0x0400,
            TPM_HORNEGANIMATION = 0x0800,
            TPM_VERPOSANIMATION = 0x1000,
            TPM_VERNEGANIMATION = 0x2000,
            TPM_NOANIMATION = 0x4000,
            TPM_LAYOUTRTL = 0x8000,
            TPM_WORKAREA = 0x10000
        }

        [Flags]
        private enum MFT : uint
        {

            GRAYED = 0x00000003,
            DISABLED = 0x00000003,
            CHECKED = 0x00000008,
            SEPARATOR = 0x00000800,
            RADIOCHECK = 0x00000200,
            BITMAP = 0x00000004,
            OWNERDRAW = 0x00000100,
            MENUBARBREAK = 0x00000020,
            MENUBREAK = 0x00000040,
            RIGHTORDER = 0x00002000,
            BYCOMMAND = 0x00000000,
            BYPOSITION = 0x00000400,
            POPUP = 0x00000010,
            ////Added
            MFT_STRING = 0x000,
            MF_BYPOSITION = 0x400,
            MF_REMOVE = 0x1000
        }

        private enum MFS : uint
        {
            MFS_ENABLED = 0x00000000,
            MFS_UNCHECKED = 0x00000000,
            MFS_UNHILITE = 0x00000000,
            MFS_GRAYED = 0x00000003,
            MFS_DISABLED = 0x00000003,
            MFS_CHECKED = 0x00000008,
            MFS_HILITE = 0x00000080,
            MFS_DEFAULT = 0x00001000
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [Flags]
        private enum MenuFlags : uint
        {
            MF_STRING = 0,
            MF_BYCOMMAND = 0,
            MF_BYPOSITION = 0x400,
            MF_SEPARATOR = 0x800,
            MF_REMOVE = 0x1000,
            MF_DISABLED = 0x00000002,
            MF_ENABLED = 0x00000000,
            MF_GRAYED = 0x00000001
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public Int32 xHotspot;
            public Int32 yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        public static Win32MenuItem ShowContextMenu(IntPtr hMainWindow, IList<Win32MenuItem> listMenu)
        {
            if (!GetCursorPos(out POINT pt))
                throw new System.ComponentModel.Win32Exception();

            var hMenu = new SafeMenuHandle(CreatePopupMenu());

            if (hMenu.IsInvalid)
                throw new System.ComponentModel.Win32Exception();

            uint menuID = 1;

            //Keep reference until end of function. Then handles are safely closed.
            var listSupressDispose = new List<SafeObjectHandle>();

            foreach (var menu in listMenu)
            {
                if (menu.Text == "-")
                {
                    if (!AppendMenu(hMenu, MenuFlags.MF_SEPARATOR, 0, null))
                        throw new System.ComponentModel.Win32Exception();
                }
                else
                {
                    if (!AppendMenu(hMenu, MenuFlags.MF_STRING, menuID, menu.Text))
                        throw new System.ComponentModel.Win32Exception();

                    if (!menu.Enabled)
                        EnableMenuItem(hMenu, menuID, MenuFlags.MF_BYCOMMAND | MenuFlags.MF_DISABLED);

                    ICONINFO ii = new ICONINFO();
                    //TODO-DNC Icons in Tray contect menu are not transparent - cosmetic
                    GetIconInfo(menu.Icon, out ii);
                    listSupressDispose.Add(new SafeObjectHandle(ii.hbmColor));
                    listSupressDispose.Add(new SafeObjectHandle(ii.hbmMask));
                    SetMenuItemBitmaps(hMenu, menuID, MFT.BYCOMMAND, ii.hbmColor, ii.hbmColor);
                }

                menuID++;
            }

            //must set window to the foreground or the menu won't disappear when it should
            SetForegroundWindow(hMainWindow);

            var selectedMenu = TrackPopupMenuEx(hMenu, TPM.TPM_RIGHTALIGN | TPM.TPM_BOTTOMALIGN | 
                TPM.TPM_RETURNCMD | TPM.TPM_RIGHTBUTTON, pt.x, pt.y, hMainWindow, IntPtr.Zero);

            return selectedMenu == 0 ? null : listMenu[selectedMenu - 1];
        }
    }

    public class Win32MenuItem : IMenuItem
    {
        public string Text { get; private set; }
        public SafeIconHandle Icon { get; private set; }
        public Action Callback { get; private set; }
        public IList<IMenuItem> SubItems { get; private set; }
        public bool Enabled { get; private set; }

        public Win32MenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems)
        {
            if (subitems != null && subitems.Count > 0)
                throw new NotImplementedException("So far not needed.");

            this.Text = text;
            this.Callback = callback;
            this.SubItems = subitems;
            this.Enabled = true;
            SetIcon(icon);
        }

        #region IMenuItem implementation
        public void SetText(string text)
        {
            this.Text = text;
        }

        public void SetIcon(MenuIcons icon)
        {
            switch (icon)
            {
                case MenuIcons.Pause:
                    this.Icon = Win32IconLoader.MenuPauseIcon;
                    break;
                case MenuIcons.Quit:
                    this.Icon = Win32IconLoader.MenuQuitIcon;
                    break;
                case MenuIcons.Resume:
                    this.Icon = Win32IconLoader.MenuPlayIcon;
                    break;
                case MenuIcons.Status:
                    this.Icon = Win32IconLoader.MenuOpenIcon;
                    break;
                case MenuIcons.None:
                default:
                    this.Icon = new SafeIconHandle(IntPtr.Zero);
                    break;
            }
        }

        public void SetEnabled(bool isEnabled)
        {
            this.Enabled = isEnabled;
        }

        public void SetDefault(bool value)
        {
            //TODO-DNC Cosmetic, not needed
        }
        #endregion
    }
}