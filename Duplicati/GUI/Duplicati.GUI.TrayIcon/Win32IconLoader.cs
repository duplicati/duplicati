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
    public class SafeIconHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public SafeIconHandle(IntPtr hIcon) : base(true)
        {
            SetHandle(hIcon);
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        protected override bool ReleaseHandle()
        {
            return DestroyIcon(handle);
        }
    }
    public static class Win32IconLoader
    {
        private const uint IMAGE_ICON = 1;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hInstance, string lpszName, uint uType, int cxDesired, int cyDesired, LR fuLoad);

        [Flags]
        private enum LR : uint
        {
            LR_DEFAULTCOLOR = 0x00000000,
            LR_MONOCHROME = 0x00000001,
            LR_COLOR = 0x00000002,
            LR_COPYRETURNORG = 0x00000004,
            LR_COPYDELETEORG = 0x00000008,
            LR_LOADFROMFILE = 0x00000010,
            LR_LOADTRANSPARENT = 0x00000020,
            LR_DEFAULTSIZE = 0x00000040,
            LR_VGACOLOR = 0x00000080,
            LR_LOADMAP3DCOLORS = 0x00001000,
            LR_CREATEDIBSECTION = 0x00002000,
            LR_COPYFROMRESOURCE = 0x00004000,
            LR_SHARED = 0x00008000
        }

        private static readonly Dictionary<string, SafeIconHandle> ICONS = new Dictionary<string, SafeIconHandle>();
        private static readonly object LOCK = new object();
        private static readonly string m_icofolder = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), @"TrayResources\WinIcons");

        private static SafeIconHandle LoadIcon(string filename)
        {
            lock (LOCK)
                if (ICONS.TryGetValue(filename, out SafeIconHandle ico))
                    return ico;
                else
                    return new SafeIconHandle(LoadImage(IntPtr.Zero, Path.Combine(m_icofolder, filename), IMAGE_ICON, 16, 16, LR.LR_LOADFROMFILE));
        }

        public static SafeIconHandle TrayNormalIcon { get { return LoadIcon("TrayNormal.ico"); } }
        public static SafeIconHandle TrayErrorIcon { get { return LoadIcon("TrayNormalError.ico"); } }
        public static SafeIconHandle TrayPauseIcon { get { return LoadIcon("TrayNormalPause.ico"); } }
        public static SafeIconHandle TrayWorkingIcon { get { return LoadIcon("TrayWorking.ico"); } }
        public static SafeIconHandle MenuPauseIcon { get { return LoadIcon("context_menu_pause.ico"); } }
        public static SafeIconHandle MenuPlayIcon { get { return LoadIcon("context_menu_play.ico"); } }
        public static SafeIconHandle MenuQuitIcon { get { return LoadIcon("context_menu_quit.ico"); } }
        public static SafeIconHandle MenuOpenIcon { get { return LoadIcon("context_menu_open.ico"); } }
    }
}