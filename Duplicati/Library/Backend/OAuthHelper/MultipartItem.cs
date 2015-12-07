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
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;

namespace Duplicati.Library
{
    public class MultipartItem
    {
        public MultipartItem()
        {
            this.Headers = new Dictionary<string, string>();
        }

        public MultipartItem(string contenttype, string name = null, string filename = null)
            : this()
        {
            ContentType = contenttype;
            if (!string.IsNullOrWhiteSpace(name))
                SetContentDisposition(name, filename);
        }

        public MultipartItem(object content, string contenttype = "application/json; charset=utf-8", string name = null, string filename = null)
            : this(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(content)), contenttype, name, filename)
        {
        }

        public MultipartItem(byte[] content, string contenttype = "application/octet-stream", string name = null, string filename = null)
            : this(new MemoryStream(content), contenttype, name, filename)
        {
        }
        public MultipartItem(Stream content, string contenttype = "application/octet-stream", string name = null, string filename = null)
            : this(contenttype, name, filename)
        {
            ContentData = content;
            Headers["Content-Length"] = content.Length.ToString();
        }

        public string ContentType { get { return Headers.ContainsKey("Content-Type") ? Headers["Content-Type"] : null; } set { Headers["Content-Type"] = value; } }
        public Stream ContentData { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string ContentTypeName { set { Headers["Content-Disposition"] = string.Format("form-data; name=\"{0}\"", Library.Utility.Uri.UrlEncode(value)); } }

        public MultipartItem SetHeaderRaw(string key, string value)
        {
            Headers[key] = value;
            return this;
        }
        public MultipartItem SetHeader(string key, string value)
        {
            return SetHeaderRaw(key, Library.Utility.Uri.UrlEncode(value));
        }
        public MultipartItem SetContentDisposition(string name, string filename = null)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                this.ContentTypeName = name;
                return this;
            }
                
            return SetHeaderRaw("Content-Disposition", string.Format("form-data; name=\"{0}\"; filename=\"{1}\"", Library.Utility.Uri.UrlEncode(name), Library.Utility.Uri.UrlEncode(filename)));
        }

    }}

