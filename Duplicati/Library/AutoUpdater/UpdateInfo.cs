//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.Library.AutoUpdater
{
    /// <summary>
    /// The different kinds of releases supported
    /// </summary>
    public enum ReleaseType
    {
        Unknown,
        Stable,
        Experimental,
        Nightly,
        Debug
    }

    public enum UpdateSeverity
    {
        None,
        Low,
        Normal,
        High,
        Critical
    }

    public class UpdateInfo
    {
        public string Displayname;
        public DateTime ReleaseTime;
        public string ReleaseType;
        public ReleaseType ReleaseTypeParsed;
        public UpdateSeverity UpdateSeverity;
        public string ChangeInfo;
        public long CompressedSize;
        public long UncompressedSize;
        public string SHA256;
        public string MD5;
        public string[] RemoteURLS;
        public FileEntry[] Files;
    }
}

