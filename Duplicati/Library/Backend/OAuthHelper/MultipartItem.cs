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

        public MultipartItem(string contenttype, string name, string filename)
            : this()
        {
            ContentType = contenttype;
            SetContentDisposition(name, filename);
        }

        public MultipartItem(object content, string name)
            : this(JsonConvert.SerializeObject(content), "application/json; charset=utf-8", name, null)
        {
        }

        public MultipartItem(string content, string contenttype, string name, string filename)
            : this(System.Text.Encoding.UTF8.GetBytes(content), contenttype, name, filename)
        {
        }

        public MultipartItem(byte[] content, string contenttype, string name, string filename)
            : this(new MemoryStream(content), contenttype, name, filename)
        {
        }

        public MultipartItem(Stream content, string name, string filename)
            : this(content, "application/octet-stream", name, filename)
        {
        }

        public MultipartItem(Stream content, string contenttype, string name, string filename)
            : this(contenttype, name, filename)
        {
            ContentData = content;
            ContentLength = content.Length;
        }

        public string ContentType 
        { 
            get 
            { 
                return Headers.ContainsKey("Content-Type") ? Headers["Content-Type"] : null; 
            } 
            set 
            { 
                if (string.IsNullOrWhiteSpace(value))
                    Headers.Remove("Content-Type");
                else
                    Headers["Content-Type"] = value; 
            } 
        }

        public Stream ContentData { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string ContentTypeName 
        {
            get
            {
                string v;
                Headers.TryGetValue("Content-Disposition", out v);
                if (string.IsNullOrWhiteSpace(v))
                    return null;
                
                var m = new System.Text.RegularExpressions.Regex("name=\"(?<name>[^\"]+)\"").Match(v);
                return m.Success ? m.Groups["name"].Value : null;
            }
            set 
            { 
                if (string.IsNullOrWhiteSpace(value))
                    Headers.Remove("Content-Disposition");
                else
                    Headers["Content-Disposition"] = string.Format("form-data; name=\"{0}\"", Library.Utility.Uri.UrlEncode(value)); 
            }
        }

        public string ContentTypeFileName 
        {
            get
            {
                string v;
                Headers.TryGetValue("Content-Disposition", out v);
                if (string.IsNullOrWhiteSpace(v))
                    return null;
                
                var m = new System.Text.RegularExpressions.Regex("filename=\"(?<name>[^\"]+)\"").Match(v);
                return m.Success ? m.Groups["name"].Value : null;
            }
        }

        public long ContentLength
        {
            get
            {
                string v;
                Headers.TryGetValue("Content-Length", out v);
                long s;
                if (long.TryParse(v, out s))
                    return s;

                return -1;
            }
            set
            {
                if (value < 0)
                    Headers.Remove("Content-Length");
                Headers["Content-Length"] = value.ToString();
            }
        }

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

