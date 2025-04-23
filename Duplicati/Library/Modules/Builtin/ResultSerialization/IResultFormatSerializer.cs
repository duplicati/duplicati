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

namespace Duplicati.Library.Modules.Builtin
{
    /// <summary>
    /// Interface for describing a result serializer
    /// </summary>
    public interface IResultFormatSerializer
    {
        /// <summary>
        /// Serialize the specified result and logLines.
        /// </summary>
        /// <returns>The serialized result string.</returns>
        /// <param name="result">The result to serialize.</param>
        /// <param name="exception">An optional failure exception, or null</param>
        /// <param name="loglines">The log lines to serialize.</param>
        /// <param name="additional">Additional parameters to include</param>
        string Serialize(object result, Exception exception, IEnumerable<string> loglines, Dictionary<string, string> additional);

        /// <summary>
        /// Returns the format that the serializer represents
        /// </summary>
        ResultExportFormat Format { get; }
    }
}
