//  Copyright (C) 2018, The Duplicati Team
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
namespace Duplicati.Library.Backend.WebApi
{
	internal static class GoogleDrive
	{
		internal static class Url
		{
			public const string DRIVE = "https://www.googleapis.com/drive/v2";
			public const string UPLOAD = "https://www.googleapis.com/upload/drive/v2";
		}

		internal static class Path
		{
			public static string File => "files";
			public static string About { get { return "about"; } }
		}

		internal static class QueryParam
		{
			public static string File => "q";
			public static string SupportsTeamDrive { get { return "supportsTeamDrives"; } }
			public static string IncludeTeamDrive { get { return "includeTeamDriveItems"; } }
			public static string PageToken { get { return "pageToken"; } }
			public static string UploadType { get { return "uploadType"; } }
			public static string Alt { get { return "alt"; } }
		}

		internal static class QueryValue
		{
			public static string True { get { return "true"; } }
			public static string False { get { return "false"; } }
			public static string Resumable { get { return "resumable"; } }
			public static string Media { get { return "media"; } }
		}

	}
}