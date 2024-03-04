// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Drawing;
using System.Collections.Generic;

namespace Duplicati.GUI.TrayIcon
{
    public static class ImageLoader
    {
        private static readonly System.Reflection.Assembly ASSEMBLY = System.Reflection.Assembly.GetExecutingAssembly();
        private static readonly string PREFIX = ASSEMBLY.GetName().Name + ".";
        private static readonly Dictionary<string, Bitmap> BITMAPS = new Dictionary<string, Bitmap>();
        private static readonly Dictionary<string, Icon> ICONS = new Dictionary<string, Icon>();
        private static readonly object LOCK = new object();

        private static Bitmap LoadImage(string filename)
        {
            Bitmap bmp;
            if (BITMAPS.TryGetValue(filename, out bmp))
                return bmp;
                
            lock(LOCK)
                if (!BITMAPS.TryGetValue(filename, out bmp))
                    return BITMAPS[filename] = (Bitmap)Image.FromStream(ASSEMBLY.GetManifestResourceStream(PREFIX + filename)); 
            
            return BITMAPS[filename];
        }

        public static Icon LoadIcon(string filename)
        {
            return LoadIcon(filename, new Size(32, 32));
        }

        public static Icon LoadIcon(string filename, Size size)
        {
            Icon ico;

            var cachename = string.Format("{0};{1}x{2}", filename, size.Width, size.Height);

            if (ICONS.TryGetValue(cachename, out ico))
                return ico;

            if (!filename.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                using(var ms = new System.IO.MemoryStream())
                {
                    Icon ic;
                    using(var bmp = LoadImage(filename))
                        ic = Icon.FromHandle(bmp.GetHicon());

                    ms.Position = 0;
                    lock(LOCK)
                        if (!ICONS.TryGetValue(cachename, out ico))
                            return ICONS[cachename] = ic;
                }
                
            lock(LOCK)
                if (!ICONS.TryGetValue(cachename, out ico))
                    return ICONS[cachename] = new Icon(ASSEMBLY.GetManifestResourceStream(PREFIX + filename), size);
            
            return ICONS[cachename];
        }


        public const string WarningIcon = "TrayResources.WinIcons.TrayNormalWarning.ico";
        public const string NormalIcon = "TrayResources.WinIcons.TrayNormal.ico";
        public const string ErrorIcon = "TrayResources.WinIcons.TrayNormalError.ico";
        public const string PauseIcon = "TrayResources.WinIcons.TrayNormalPause.ico";
        public const string WorkingIcon = "TrayResources.WinIcons.TrayWorking.ico";

        public static Bitmap Pause { get { return LoadImage("TrayResources.WinIcons.context_menu_pause.ico"); } }
        public static Bitmap Play { get { return LoadImage("TrayResources.WinIcons.context_menu_play.ico"); } }
        public static Bitmap CloseMenuIcon { get { return LoadImage("TrayResources.WinIcons.context_menu_quit.ico"); } }
        public static Bitmap StatusMenuIcon { get { return LoadImage("TrayResources.WinIcons.context_menu_open.ico"); } }
        
    }
}

