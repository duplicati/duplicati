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

using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Parity
{
    /// <summary>
    /// Simple helper base class for parity modules that provides the disposable
    /// plumbing and declares the module metadata that each implementation supplies.
    /// </summary>
    public abstract class ParityBase : IParity
    {
        #region IParity Members

        public abstract IList<ICommandLineArgument> SupportedCommands { get; }
        public abstract string FilenameExtension { get; }
        public abstract string DisplayName { get; }
        public abstract string Description { get; }
        public abstract bool IsAvailable { get; }

        public abstract void Create(string inputfile, string parityfile);
        public abstract bool Verify(string inputfile, string parityfile);
        public abstract bool Repair(string inputfile, string parityfile);

        protected abstract void Dispose(bool disposing);

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}
