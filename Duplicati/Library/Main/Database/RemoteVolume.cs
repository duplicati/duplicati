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

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// Represents a remote volume with its name, hash, size, and an optional file entry.
    /// Implements the IRemoteVolume interface.
    /// </summary>
    public class RemoteVolume : IRemoteVolume
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteVolume"/> class with a file
        /// entry and an optional hash.
        /// </summary>
        /// <param name="file">The file entry associated with the remote volume.</param>
        /// <param name="hash">The hash of the remote volume, can be null.</param>
        public RemoteVolume(Interface.IFileEntry file, string hash = null)
        {
            Name = file.Name;
            Size = file.Size;
            Hash = hash;
            File = file;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteVolume"/> class with a name,
        /// hash, and size.
        /// </summary>
        /// <param name="name">The name of the remote volume.</param>
        /// <param name="hash">The hash of the remote volume.</param>
        /// <param name="size">The size of the remote volume in bytes.</param>
        public RemoteVolume(string name, string hash, long size)
        {
            Name = name;
            Hash = hash;
            Size = size;
            File = null;
        }

        public string Name { get; private set; }
        public string Hash { get; private set; }
        public long Size { get; private set; }
        public Interface.IFileEntry File { get; private set; }
    }
}
