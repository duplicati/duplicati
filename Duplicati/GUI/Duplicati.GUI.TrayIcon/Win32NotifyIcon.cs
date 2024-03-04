using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Duplicati.GUI.TrayIcon
{
    public static class Win32NativeNotifyIcon
    {
        public const int WM_APP = 0x8000;

        public enum NotifyFlags : int
        {
            NIF_MESSAGE = 0x00000001,
            NIF_ICON = 0x00000002,
            NIF_TIP = 0x00000004,
            NIF_STATE = 0x00000008,
            NIF_INFO = 0x00000010,
            NIF_GUID = 0x00000020,
            NIF_REALTIME = 0x00000040,
            NIF_SHOWTIP = 0x00000080
        }

        public enum NotifyIconMessage : int
        {
            NIM_ADD = 0x00000000,
            NIM_MODIFY = 0x00000001,
            NIM_DELETE = 0x00000002,
            NIM_SETFOCUS = 0x00000003,
            NIM_SETVERSION = 0x00000004
        }

        public enum InfoFlags : int
        {
            NIIF_NONE = 0x00000000,
            NIIF_INFO = 0x00000001,
            NIIF_WARNING = 0x00000002,
            NIIF_ERROR = 0x00000003,
            NIIF_USER = 0x00000004,
            NIIF_NOSOUND = 0x00000010,
            NIIF_LARGE_ICON = 0x00000020,
            NIIF_RESPECT_QUIET_TIME = 0x00000080
        }

        public const int NOTIFYICON_VERSION_4 = 4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public NotifyFlags uFlags;
            public int uCallbackMessage;
            public SafeIconHandle hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public InfoFlags dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonicon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern int Shell_NotifyIcon(NotifyIconMessage dwMessage, NOTIFYICONDATA pNID);
    }

    public class Win32NotifyIcon
    {
        public int ID { get; private set; }

        public int MessageID { get; private set; }

        public IntPtr MainWindowHandle { get; private set; }

        public SafeIconHandle Icon { get; private set; }

        public string ToolTip { get; private set; }

        public Win32NotifyIcon(IntPtr MainWindowHandle, int ID, int MessageID, SafeIconHandle Icon, string ToolTip)
        {
            if (MainWindowHandle == IntPtr.Zero | ID == 0 | MessageID == 0)
                throw new ArgumentException("Bad input arguments Win32NotifyIcon::ctor");

            this.MainWindowHandle = MainWindowHandle;
            this.ID = ID;
            this.MessageID = MessageID;
            this.Icon = Icon;
            this.ToolTip = ToolTip;
        }

        public void Create()
        {
            var nid = new Win32NativeNotifyIcon.NOTIFYICONDATA
            {
                hIcon = Icon,
                hWnd = MainWindowHandle,
                uID = ID,
                szTip = ToolTip,
                uFlags = Win32NativeNotifyIcon.NotifyFlags.NIF_ICON | Win32NativeNotifyIcon.NotifyFlags.NIF_TIP | 
                    Win32NativeNotifyIcon.NotifyFlags.NIF_MESSAGE | Win32NativeNotifyIcon.NotifyFlags.NIF_SHOWTIP,
                uCallbackMessage = MessageID,
                cbSize = Marshal.SizeOf(typeof(Win32NativeNotifyIcon.NOTIFYICONDATA))
            };

            if (Win32NativeNotifyIcon.Shell_NotifyIcon(Win32NativeNotifyIcon.NotifyIconMessage.NIM_ADD, nid) == 0)
                throw new System.ComponentModel.Win32Exception();

            nid.uTimeoutOrVersion = Win32NativeNotifyIcon.NOTIFYICON_VERSION_4;

            if (Win32NativeNotifyIcon.Shell_NotifyIcon(Win32NativeNotifyIcon.NotifyIconMessage.NIM_SETVERSION, nid) == 0)
                throw new System.ComponentModel.Win32Exception();
        }

        public void Delete()
        {
            var nid = new Win32NativeNotifyIcon.NOTIFYICONDATA
            {
                hWnd = MainWindowHandle,
                uID = ID,
                hIcon = new SafeIconHandle(IntPtr.Zero),
                cbSize = Marshal.SizeOf(typeof(Win32NativeNotifyIcon.NOTIFYICONDATA))
            };

            if (Win32NativeNotifyIcon.Shell_NotifyIcon(Win32NativeNotifyIcon.NotifyIconMessage.NIM_DELETE, nid) == 0)
                throw new System.ComponentModel.Win32Exception();
        }

        public void SetIcon(SafeIconHandle hIcon)
        {
            var nid = new Win32NativeNotifyIcon.NOTIFYICONDATA
            {
                hIcon = hIcon,
                hWnd = MainWindowHandle,
                uID = ID,
                uFlags = Win32NativeNotifyIcon.NotifyFlags.NIF_ICON,
                cbSize = Marshal.SizeOf(typeof(Win32NativeNotifyIcon.NOTIFYICONDATA))
            };

            if (Win32NativeNotifyIcon.Shell_NotifyIcon(Win32NativeNotifyIcon.NotifyIconMessage.NIM_MODIFY, nid) == 0)
                throw new System.ComponentModel.Win32Exception();

            Icon = hIcon;
        }

        public void ShowBalloonTip(string BallonTitle, string BallonMessage, Win32NativeNotifyIcon.InfoFlags NotificationType)
        {
            var nid = new Win32NativeNotifyIcon.NOTIFYICONDATA
            {
                hIcon = Icon,
                hWnd = MainWindowHandle,
                uID = ID,
                cbSize = Marshal.SizeOf(typeof(Win32NativeNotifyIcon.NOTIFYICONDATA)),
                szInfoTitle = BallonTitle,
                szInfo = BallonMessage,
                dwInfoFlags = NotificationType,
                uFlags = Win32NativeNotifyIcon.NotifyFlags.NIF_INFO
            };

            if (Win32NativeNotifyIcon.Shell_NotifyIcon(Win32NativeNotifyIcon.NotifyIconMessage.NIM_MODIFY, nid) == 0)
                throw new System.ComponentModel.Win32Exception();
        }
    }
}