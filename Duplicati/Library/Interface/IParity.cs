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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Public interface for a parity (error-correction) method.
    /// A parity module produces a single parity file for a single input file,
    /// and can later verify or repair that input file using the parity file.
    /// The classes that implement this interface MUST also
    /// implement a default constructor and a constructor that
    /// has the signature new(Dictionary&lt;string, string&gt; options).
    /// The default constructor is used to construct an instance
    /// so the DisplayName and other values can be read.
    /// The other constructor is used to do the actual work.
    /// </summary>
    public interface IParity : IDynamicModule, IDisposable
    {
        /// <summary>
        /// Creates parity data for <paramref name="inputfile"/>, writing a single
        /// parity file to <paramref name="parityfile"/>.
        /// </summary>
        /// <param name="inputfile">The file to protect</param>
        /// <param name="parityfile">The parity file to create</param>
        /// <param name="cancellationToken">The cancellation token</param>
        Task CreateAsync(string inputfile, string parityfile, CancellationToken cancellationToken);

        /// <summary>
        /// Verifies the integrity of <paramref name="inputfile"/> using the parity data.
        /// </summary>
        /// <param name="inputfile">The file to verify</param>
        /// <param name="parityfile">The parity file protecting the input file</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns><c>true</c> if the input file is intact</returns>
        Task<bool> VerifyAsync(string inputfile, string parityfile, CancellationToken cancellationToken);

        /// <summary>
        /// Attempts to repair <paramref name="inputfile"/> in place using the parity data.
        /// </summary>
        /// <param name="inputfile">The file to repair, modified in place if repairable</param>
        /// <param name="parityfile">The parity file protecting the input file</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns><c>true</c> if the file is intact after the operation</returns>
        Task<bool> RepairAsync(string inputfile, string parityfile, CancellationToken cancellationToken);

        /// <summary>
        /// Returns whether the underlying parity engine is available for use
        /// (for example, whether the required external program was found).
        /// When this is <c>false</c>, parity operations are skipped rather than failing.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// The extension that the parity implementation adds to the filename
        /// </summary>
        string FilenameExtension { get; }

        /// <summary>
        /// A localized string describing the parity module with a friendly name
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// A localized description of the parity module
        /// </summary>
        string Description { get; }
    }
}
