// Copyright (C) 2026, The Duplicati Team
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

#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Stores files the same way <see cref="NoFreeSpaceBackend"/> does, but answers that it
    /// does not know what the destination has room for.
    ///
    /// A destination can be perfectly writable and still have no capacity to report: a Google
    /// shared drive has no quota of its own, which is what issue #4230 was about.
    /// </summary>
    public class UnknownQuotaBackend : NoFreeSpaceBackend
    {
        public UnknownQuotaBackend()
        {
        }

        public UnknownQuotaBackend(string url, Dictionary<string, string> options)
            : base(url, options)
        {
        }

        public override string ProtocolKey => "unknownquota";

        public override Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken)
            => Task.FromResult<IQuotaInfo?>(null);
    }
}
