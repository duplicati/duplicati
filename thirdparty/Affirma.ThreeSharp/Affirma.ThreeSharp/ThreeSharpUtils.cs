/******************************************************************************* 
 *  Licensed under the Apache License, Version 2.0 (the "License"); 
 *  
 *  You may not use this file except in compliance with the License. 
 *  You may obtain a copy of the License at: http://www.apache.org/licenses/LICENSE-2.0.html 
 *  This file is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
 *  CONDITIONS OF ANY KIND, either express or implied. See the License for the 
 *  specific language governing permissions and limitations under the License.
 * ***************************************************************************** 
 * 
 *  Joel Wetzel
 *  Affirma Consulting
 *  jwetzel@affirmaconsulting.com
 * 
 */

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using System.Xml.Serialization;

using Affirma.ThreeSharp.Model;

namespace Affirma.ThreeSharp
{
    public enum CallingFormat
    {
        REGULAR,        // http://s3.amazonaws.com/key
        SUBDOMAIN,      // http://bucket.s3.amazonaws.com/key
        VANITY          // http://mydomain.com/key -- a vanity domain which resolves to s3.amazonaws.com
    }

    public class ThreeSharpStringComparer : IComparer
    {
        private CompareInfo compareInfo;
        private CompareOptions compareOptions = CompareOptions.None;

        // Constructs a comparer using the specified CompareOptions.
        public ThreeSharpStringComparer(CompareInfo compareInfo, CompareOptions compareOptions)
        {
            this.compareInfo = compareInfo;
            this.compareOptions = compareOptions;
        }

        // Compares strings with the CompareOptions specified in the constructor.
        public int Compare(Object a, Object b)
        {
            if (a == b) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            String sa = a as String;
            String sb = b as String;
            if (sa != null && sb != null)
                return this.compareInfo.Compare(sa, sb, this.compareOptions);

            throw new ArgumentException("a and b must be strings.");
        }
    }

    public class ThreeSharpUtils
    {
        public static readonly string METADATA_PREFIX = "x-amz-meta-";
        public static readonly string AMAZON_HEADER_PREFIX = "x-amz-";
        public static readonly string ALTERNATIVE_DATE_HEADER = "x-amz-date";
        public static readonly string DATE_HEADER = "DATE";


        public static string MakeCanonicalString(string bucket, string key, WebRequest request)
        {
            return MakeCanonicalString(bucket, key, new SortedList(), request);
        }
        
        public static string MakeCanonicalString(string bucket, string key, SortedList query, WebRequest request)
        {
            SortedList headers = new SortedList();
            foreach (string header in request.Headers)
            {
                headers.Add(header, request.Headers[header]);
            }
            if (headers["Content-Type"] == null)
            {
                headers.Add("Content-Type", request.ContentType);
            }
            return MakeCanonicalString(request.Method, bucket, key, query, headers, null);
        }

        public static string MakeCanonicalString(string verb, string bucketName, string key, SortedList queryParams, SortedList headers, string expires)
        {
            StringBuilder buf = new StringBuilder();
            buf.Append(verb);
            buf.Append("\n");

            ThreeSharpStringComparer comparer = new ThreeSharpStringComparer(CompareInfo.GetCompareInfo(""), CompareOptions.StringSort);
            SortedList interestingHeaders = new SortedList(comparer);

            if (headers != null)
            {
                foreach (string header in headers.Keys)
                {
                    string lk = header.ToLower();
                    if (lk.Equals("content-type") ||
                         lk.Equals("content-md5") ||
                         lk.Equals("date") ||
                         lk.StartsWith(AMAZON_HEADER_PREFIX))
                    {
                        interestingHeaders.Add(lk, headers[header]);
                    }
                }
            }
            if (interestingHeaders[ALTERNATIVE_DATE_HEADER] != null)
            {
                interestingHeaders.Add("date", "");
            }

            // if the expires is non-null, use that for the date field.  this
            // trumps the x-amz-date behavior.
            if (expires != null)
            {
                interestingHeaders.Add("date", expires);
            }

            // these headers require that we still put a new line after them,
            // even if they don't exist.
            {
                string[] newlineHeaders = { "content-type", "content-md5" };
                foreach (string header in newlineHeaders)
                {
                    if (interestingHeaders.IndexOfKey(header) == -1)
                    {
                        interestingHeaders.Add(header, "");
                    }
                }
            }

            // Finally, add all the interesting headers (i.e.: all that startwith x-amz- ;-))
            foreach (string header in interestingHeaders.Keys)
            {
                if (header.StartsWith(AMAZON_HEADER_PREFIX))
                {
                    buf.Append(header).Append(":").Append((interestingHeaders[header] as string).Trim());
                }
                else
                {
                    buf.Append(interestingHeaders[header]);
                }
                buf.Append("\n");
            }

            // Build the path using the bucket and key
            buf.Append("/");
            if (bucketName != null && !bucketName.Equals(""))
            {
                buf.Append(bucketName);
                buf.Append("/");
            }

            // Append the key (it may be an empty string)
            if (key != null && key.Length != 0)
            {
                buf.Append(key);
            }

            // if there is an acl, logging, or torrent parameter, add them to the string.
            if (queryParams != null)
            {
                if (queryParams.IndexOfKey("acl") != -1)
                {
                    buf.Append("?acl");
                }
                else if (queryParams.IndexOfKey("torrent") != -1)
                {
                    buf.Append("?torrent");
                }
                else if (queryParams.IndexOfKey("logging") != -1)
                {
                    buf.Append("?logging");
                }
            }

            return buf.ToString();
        }
        
        public static string Encode(string awsSecretAccessKey, string canonicalString, bool urlEncode)
        {
            Encoding ae = new UTF8Encoding();
            HMACSHA1 signature = new HMACSHA1(ae.GetBytes(awsSecretAccessKey));
            string b64 = Convert.ToBase64String(signature.ComputeHash(ae.GetBytes(canonicalString.ToCharArray())));

            if (urlEncode)
            {
                return HttpUtility.UrlEncode(b64);
            }
            else
            {
                return b64;
            }
        }
               
        public static string GetHttpDate()
        {
            // Setting the Culture will ensure we get a proper HTTP Date.
            string date = System.DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss ", System.Globalization.CultureInfo.InvariantCulture) + "GMT";
            return date;
        }
        
        public static long CurrentTimeMillis()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }
        
        /// <summary>
        /// Calculates the endpoint based on the calling format.
        /// </summary>
        public static string BuildUrlBase(string server, int port, string bucket, CallingFormat format)
        {
            StringBuilder endpoint = new StringBuilder();

            if (format == CallingFormat.REGULAR)
            {
                endpoint.Append(server);
                endpoint.Append(":");
                endpoint.Append(port);

                if (bucket != null && !bucket.Equals(""))
                {
                    endpoint.Append("/");
                    endpoint.Append(bucket);
                }
            }
            else if (format == CallingFormat.SUBDOMAIN)
            {
                if (!string.IsNullOrEmpty(bucket))
                {
                    endpoint.Append(bucket);
                    endpoint.Append(".");
                }

                endpoint.Append(server);
                endpoint.Append(":");
                endpoint.Append(port);
            }
            else if (format == CallingFormat.VANITY)
            {
                endpoint.Append(bucket);
                endpoint.Append(":");
                endpoint.Append(port);
            }
            endpoint.Append("/");
            return endpoint.ToString();
        }
                
        public static string ConvertQueryListToQueryString(SortedList queryList)
        {
            StringBuilder queryString = new StringBuilder();
            bool firstParameter = true;
            if (queryList != null)
            {
                foreach (string key in queryList.Keys)
                {
                    string argument = key;
                    if (firstParameter)
                    {
                        firstParameter = false;
                        queryString.Append("?");
                    }
                    else
                    {
                        queryString.Append("&");
                    }

                    queryString.Append(key);
                    string value = (string)queryList[key];
                    if (value != null && value.Length != 0)
                    {
                        queryString.Append("=");
                        queryString.Append(value);
                    }
                }
            }
            return queryString.ToString();
        }
                
        public static string ConvertExtensionToMimeType(string extension)
        {
            switch (extension)
            {
                case ".ai": return "application/postscript";
                case ".aif": return "audio/x-aiff";
                case ".aifc": return "audio/x-aiff";
                case ".aiff": return "audio/x-aiff";
                case ".asc": return "text/plain";
                case ".au": return "audio/basic";
                case ".avi": return "video/x-msvideo";
                case ".bcpio": return "application/x-bcpio";
                case ".bin": return "application/octet-stream";
                case ".c": return "text/plain";
                case ".cc": return "text/plain";
                case ".ccad": return "application/clariscad";
                case ".cdf": return "application/x-netcdf";
                case ".class": return "application/octet-stream";
                case ".cpio": return "application/x-cpio";
                case ".cpp": return "text/plain";
                case ".cpt": return "application/mac-compactpro";
                case ".cs": return "text/plain";
                case ".csh": return "application/x-csh";
                case ".css": return "text/css";
                case ".dcr": return "application/x-director";
                case ".dir": return "application/x-director";
                case ".dms": return "application/octet-stream";
                case ".doc": return "application/msword";
                case ".drw": return "application/drafting";
                case ".dvi": return "application/x-dvi";
                case ".dwg": return "application/acad";
                case ".dxf": return "application/dxf";
                case ".dxr": return "application/x-director";
                case ".eps": return "application/postscript";
                case ".etx": return "text/x-setext";
                case ".exe": return "application/octet-stream";
                case ".ez": return "application/andrew-inset";
                case ".f": return "text/plain";
                case ".f90": return "text/plain";
                case ".fli": return "video/x-fli";
                case ".gif": return "image/gif";
                case ".gtar": return "application/x-gtar";
                case ".gz": return "application/x-gzip";
                case ".h": return "text/plain";
                case ".hdf": return "application/x-hdf";
                case ".hh": return "text/plain";
                case ".hqx": return "application/mac-binhex40";
                case ".htm": return "text/html";
                case ".html": return "text/html";
                case ".ice": return "x-conference/x-cooltalk";
                case ".ief": return "image/ief";
                case ".iges": return "model/iges";
                case ".igs": return "model/iges";
                case ".ips": return "application/x-ipscript";
                case ".ipx": return "application/x-ipix";
                case ".jpe": return "image/jpeg";
                case ".jpeg": return "image/jpeg";
                case ".jpg": return "image/jpeg";
                case ".js": return "application/x-javascript";
                case ".kar": return "audio/midi";
                case ".latex": return "application/x-latex";
                case ".lha": return "application/octet-stream";
                case ".lsp": return "application/x-lisp";
                case ".lzh": return "application/octet-stream";
                case ".m": return "text/plain";
                case ".man": return "application/x-troff-man";
                case ".me": return "application/x-troff-me";
                case ".mesh": return "model/mesh";
                case ".mid": return "audio/midi";
                case ".midi": return "audio/midi";
                case ".mime": return "www/mime";
                case ".mov": return "video/quicktime";
                case ".movie": return "video/x-sgi-movie";
                case ".mp2": return "audio/mpeg";
                case ".mp3": return "audio/mpeg";
                case ".mpe": return "video/mpeg";
                case ".mpeg": return "video/mpeg";
                case ".mpg": return "video/mpeg";
                case ".mpga": return "audio/mpeg";
                case ".ms": return "application/x-troff-ms";
                case ".msh": return "model/mesh";
                case ".nc": return "application/x-netcdf";
                case ".oda": return "application/oda";
                case ".pbm": return "image/x-portable-bitmap";
                case ".pdb": return "chemical/x-pdb";
                case ".pdf": return "application/pdf";
                case ".pgm": return "image/x-portable-graymap";
                case ".pgn": return "application/x-chess-pgn";
                case ".png": return "image/png";
                case ".pnm": return "image/x-portable-anymap";
                case ".pot": return "application/mspowerpoint";
                case ".ppm": return "image/x-portable-pixmap";
                case ".pps": return "application/mspowerpoint";
                case ".ppt": return "application/mspowerpoint";
                case ".ppz": return "application/mspowerpoint";
                case ".pre": return "application/x-freelance";
                case ".prt": return "application/pro_eng";
                case ".ps": return "application/postscript";
                case ".qt": return "video/quicktime";
                case ".ra": return "audio/x-realaudio";
                case ".ram": return "audio/x-pn-realaudio";
                case ".ras": return "image/cmu-raster";
                case ".rgb": return "image/x-rgb";
                case ".rm": return "audio/x-pn-realaudio";
                case ".roff": return "application/x-troff";
                case ".rpm": return "audio/x-pn-realaudio-plugin";
                case ".rtf": return "text/rtf";
                case ".rtx": return "text/richtext";
                case ".scm": return "application/x-lotusscreencam";
                case ".set": return "application/set";
                case ".sgm": return "text/sgml";
                case ".sgml": return "text/sgml";
                case ".sh": return "application/x-sh";
                case ".shar": return "application/x-shar";
                case ".silo": return "model/mesh";
                case ".sit": return "application/x-stuffit";
                case ".skd": return "application/x-koan";
                case ".skm": return "application/x-koan";
                case ".skp": return "application/x-koan";
                case ".skt": return "application/x-koan";
                case ".smi": return "application/smil";
                case ".smil": return "application/smil";
                case ".snd": return "audio/basic";
                case ".sol": return "application/solids";
                case ".spl": return "application/x-futuresplash";
                case ".src": return "application/x-wais-source";
                case ".step": return "application/STEP";
                case ".stl": return "application/SLA";
                case ".stp": return "application/STEP";
                case ".sv4cpio": return "application/x-sv4cpio";
                case ".sv4crc": return "application/x-sv4crc";
                case ".swf": return "application/x-shockwave-flash";
                case ".t": return "application/x-troff";
                case ".tar": return "application/x-tar";
                case ".tcl": return "application/x-tcl";
                case ".tex": return "application/x-tex";
                case ".tif": return "image/tiff";
                case ".tiff": return "image/tiff";
                case ".tr": return "application/x-troff";
                case ".tsi": return "audio/TSP-audio";
                case ".tsp": return "application/dsptype";
                case ".tsv": return "text/tab-separated-values";
                case ".txt": return "text/plain";
                case ".unv": return "application/i-deas";
                case ".ustar": return "application/x-ustar";
                case ".vcd": return "application/x-cdlink";
                case ".vda": return "application/vda";
                case ".vrml": return "model/vrml";
                case ".wav": return "audio/x-wav";
                case ".wrl": return "model/vrml";
                case ".xbm": return "image/x-xbitmap";
                case ".xlc": return "application/vnd.ms-excel";
                case ".xll": return "application/vnd.ms-excel";
                case ".xlm": return "application/vnd.ms-excel";
                case ".xls": return "application/vnd.ms-excel";
                case ".xlw": return "application/vnd.ms-excel";
                case ".xml": return "text/xml";
                case ".xpm": return "image/x-xpixmap";
                case ".xwd": return "image/x-xwindowdump";
                case ".xyz": return "chemical/x-pdb";
                case ".zip": return "application/zip";
                default: return "text/plain";
            }
        }                    
        
    }
}
