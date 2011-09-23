#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections;
using System.Drawing;
using System.Windows.Forms;

namespace OSGeo.MapGuide.Maestro.ResourceEditors
{
	/// <summary>
	/// Helper class for Shell Icons
	/// </summary>
	public class ShellIcons
	{
		private static ImageList m_imageList;
		private static Hashtable m_loadedImages;
		private static bool m_supported = false;

        static ShellIcons()
        {
            m_imageList = new ImageList();
            m_loadedImages = new Hashtable();
            m_imageList.ImageSize = new Size(16, 16);
            m_imageList.ColorDepth = ColorDepth.Depth32Bit;
            //m_imageList.Images.Add(System.Drawing.Image.FromStream(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(ResourceDataEditor), "blank_icon.gif")));
            m_loadedImages.Add("", 0);

            try
            {
                //This does nothing on windows, but fails on linux
                DestroyIcon(IntPtr.Zero);
                m_supported = true;
            }
            catch
            {
                m_supported = false;
            }
        }

		#region Win32 Native Code
		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
			private struct SHFILEINFO
		{
			public IntPtr hIcon;
			public IntPtr iIcon;
			[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst=260)]
			public string szDisplayName;
			[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst=80)]
			public string szTypeName;
		}

		[System.Runtime.InteropServices.DllImport("shell32.dll")]
		private static extern IntPtr SHGetFileInfo(string pszPath, int dwFileAttributes, ref SHFILEINFO psfi, int cbSizeFileInfo, int uFlags);


		[System.Runtime.InteropServices.DllImport("User32.dll")]
		private static extern int DestroyIcon(IntPtr hIcon);

		private const int SHGFI_ICON = 256;
		private const int SHGFI_LARGEICON = 0;
		private const int SHGFI_SMALLICON = 1;
		private const int SHGFI_USEFILEATTRIBUTES  = 16;
		private const int SHGFI_OPENICON = 2;
		private const int SHGFI_SELECTED = 65536;

		private const int FILE_ATTRIBUTE_NORMAL = 128;
		private const int FILE_ATTRIBUTE_DIRECTORY = 16;

		#endregion

        public static bool Supported { get { return m_supported; } }

        public static ImageList ImageList { get { return m_imageList; } }

		public static void AddIcon(string filename, System.Drawing.Image img)
		{
			string type = System.IO.Path.GetExtension(filename).ToLower();
			if (!m_loadedImages.Contains(type.ToLower()))
			{
				ShellIcons.ImageList.Images.Add(img);
				m_loadedImages.Add(type.ToLower(), ShellIcons.ImageList.Images.Count - 1);
			}
		}

        public static int GetFolderIcon(bool open)
        {
            return GetShellIcon("folder", FILE_ATTRIBUTE_DIRECTORY, SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | SHGFI_SMALLICON | (open ? SHGFI_OPENICON : 0));
        }

        public static int GetShellIcon(string filename)
        {
            return GetShellIcon(System.IO.Path.GetExtension(filename).ToLower(), FILE_ATTRIBUTE_NORMAL, SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | SHGFI_SMALLICON);
        }

		private static int GetShellIcon(string ext, int type, int attributes)
		{
			if (!m_loadedImages.Contains(ext) && m_supported)
			{
				try
				{
					SHFILEINFO shinfo = new SHFILEINFO();
					SHGetFileInfo(ext, type, ref shinfo, System.Runtime.InteropServices.Marshal.SizeOf(shinfo), attributes);
					System.Drawing.Icon tmp = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(shinfo.hIcon).Clone();
					DestroyIcon(shinfo.hIcon);
					
					m_imageList.Images.Add(tmp);
					m_loadedImages.Add(ext, m_imageList.Images.Count - 1);
				}
				catch
				{
				}
			}

			if (m_loadedImages.ContainsKey(ext))
				return (int)m_loadedImages[ext];
			else
				return 0;
		}

	}
}
