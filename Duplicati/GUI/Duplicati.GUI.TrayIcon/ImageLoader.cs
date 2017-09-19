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

        public const string NormalIcon = "Resources.TrayNormal.ico";
        public const string ErrorIcon = "Resources.TrayNormalError.ico";
        public const string PauseIcon = "Resources.TrayNormalPause.ico";
        public const string WorkingIcon = "Resources.TrayWorking.ico";

        public static Bitmap Pause { get { return LoadImage("Resources.context_menu_pause.ico"); } }
        public static Bitmap Play { get { return LoadImage("Resources.context_menu_play.ico"); } }
        public static Bitmap CloseMenuIcon { get { return LoadImage("Resources.context_menu_quit.ico"); } }
        public static Bitmap StatusMenuIcon { get { return LoadImage("Resources.context_menu_open.ico"); } }
        
    }
}

