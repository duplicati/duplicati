// Copyright (C) 2025, The Duplicati Team
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

