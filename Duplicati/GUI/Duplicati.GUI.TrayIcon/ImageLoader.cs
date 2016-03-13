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
    public static class ImageLoaderMac
    {
        public static Icon LoadFromStream(System.IO.Stream s)
        {
            return null;
        }
    }
    
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

        private static Icon LoadIcon(string filename)
        {
            Icon ico;
            if (ICONS.TryGetValue(filename, out ico))
                return ico;

            if (!filename.EndsWith(".ico", StringComparison.InvariantCultureIgnoreCase))
                using(var ms = new System.IO.MemoryStream())
                {
                    Icon ic;
                    using(var bmp = LoadImage(filename))
                        ic = Icon.FromHandle(bmp.GetHicon());

                    ms.Position = 0;
                    lock(LOCK)
                        if (!ICONS.TryGetValue(filename, out ico))
                            return ICONS[filename] = ic;
                }
                
            lock(LOCK)
                if (!ICONS.TryGetValue(filename, out ico))
                    return ICONS[filename] = new Icon(ASSEMBLY.GetManifestResourceStream(PREFIX + filename));
            
            return ICONS[filename];
        }

        public static Icon TrayNormal { get { return LoadIcon(Duplicati.Library.Utility.Utility.IsClientOSX ? "OSX_Icons.normal.png" : "Resources.TrayNormal.ico"); } }
        public static Icon TrayNormalError { get { return LoadIcon(Duplicati.Library.Utility.Utility.IsClientOSX ? "OSX_Icons.normal-error.png" : "Resources.TrayNormalError.ico"); } }
        public static Icon TrayNormalPause { get { return LoadIcon(Duplicati.Library.Utility.Utility.IsClientOSX ? "OSX_Icons.normal-pause.png" : "Resources.TrayNormalPause.ico"); } }
        public static Icon TrayNormalWarning { get { return LoadIcon(Duplicati.Library.Utility.Utility.IsClientOSX ? "OSX_Icons.normal-error.png" : "Resources.TrayNormalError.ico"); } }
        public static Icon TrayWorking { get { return LoadIcon(Duplicati.Library.Utility.Utility.IsClientOSX ? "OSX_Icons.normal-running.png" : "Resources.TrayWorking.ico"); } }
        public static Icon TrayWorkingPause { get { return LoadIcon(Duplicati.Library.Utility.Utility.IsClientOSX ? "OSX_Icons.normal-pause.png" : "Resources.TrayWorkingPause.ico"); } }
        
        public static Bitmap Pause { get { return LoadImage("Resources.Pause.png"); } }
        public static Bitmap Play { get { return LoadImage("Resources.Play.png"); } }
        public static Bitmap CloseMenuIcon { get { return LoadImage("Resources.CloseMenuIcon.png"); } }
        public static Bitmap StatusMenuIcon { get { return LoadImage("Resources.StatusMenuIcon.png"); } }
        
    }
}

