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
using NUnit.Framework;
using Duplicati.Library.Backend.WebApi;

namespace Duplicati.UnitTest
{

    public static class WebApiUrlTests
    {
        [Test]
        [Category("WebApi")]
        public static void GoogleCloudPutUrl()
        {
            string bucketId = "my_bucket";
            string putUrl = $"https://www.googleapis.com/upload/storage/v1/b/{bucketId}/o?uploadType=resumable";
            Assert.AreEqual(putUrl, GoogleCloudStorage.PutUrl(bucketId));
        }

        [Test]
        [Category("WebApi")]
        public static void CreateFolderUrl()
        {
            var url = "https://www.googleapis.com/drive/v2/files?supportsTeamDrives=true";
            Assert.AreEqual(url, GoogleDrive.CreateFolderUrl(true));
        }

        [Test]
        [Category("WebApi")]
        public static void AboutInfoUrl()
        {
            var url = "https://www.googleapis.com/drive/v2/about";
            Assert.AreEqual(url, GoogleDrive.AboutInfoUrl());
        }
    }
}
