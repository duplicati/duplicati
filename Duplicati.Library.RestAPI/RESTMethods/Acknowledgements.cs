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
using System.Collections.Generic;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Acknowledgements : IRESTMethodGET, IRESTMethodDocumented
    {
        private class GetResponse
        {
            public string Status;
            public string Acknowledgements;
        }

        public void GET(string key, RequestInfo info)
        {
            var path = SystemIO.IO_OS.PathCombine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "acknowledgements.txt");
            info.OutputOK(new GetResponse() {
                Status = "OK",
                Acknowledgements = System.IO.File.ReadAllText(path)
            });            
        }
        public string Description { get { return "Gets all acknowledgements"; } }

        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(GetResponse)),
                };
            }
        }
    }
}

