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

using Newtonsoft.Json;

namespace Duplicati.Library.Backend.OpenStack;
internal class Keystone3AuthResponse
{
    public TokenClass? token { get; set; }

    public class EndpointItem
    {
        // 'interface' is a reserved keyword, so we need this decorator to map it
        [JsonProperty(PropertyName = "interface")]
        public string? interface_name { get; set; }
        public string? region { get; set; }
        public string? url { get; set; }
    }

    public class CatalogItem
    {
        public EndpointItem[]? endpoints { get; set; }
        public string? name { get; set; }
        public string? type { get; set; }
    }
    public class TokenClass
    {
        public CatalogItem[]? catalog { get; set; }
        public DateTime? expires_at { get; set; }
    }
}