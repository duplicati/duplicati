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
using Duplicati.Proprietary.DiskImage;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Linux implementation of <see cref="IDiskImageHelper"/> using loop devices and standard Linux tools.
    /// This is a stub implementation that throws <see cref="NotImplementedException"/> for all operations.
    /// </summary>
    internal class LinuxDiskImageHelper : IDiskImageHelper
    {
        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public string CreateDisk(string imagePath, long sizeB) => throw new NotImplementedException();

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public string[] InitializeDisk(string diskIdentifier, PartitionTableType tableType, (FileSystemType, long)[] partitions) => throw new NotImplementedException();

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public string[] Mount(string diskIdentifier, string? baseMountPath = null, bool readOnly = false) => throw new NotImplementedException();

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public void Unmount(string diskIdentifier) => throw new NotImplementedException();

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public void CleanupDisk(string imagePath, string? diskIdentifier = null) => throw new NotImplementedException();

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public bool HasRequiredPrivileges() => throw new NotImplementedException();

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public PartitionTableGeometry GetPartitionTable(string diskIdentifier) => throw new NotImplementedException();

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public PartitionGeometry[] GetPartitions(string diskIdentifier) => throw new NotImplementedException();

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented
        public void FlushDisk(string diskIdentifier) => throw new NotImplementedException();

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public string ReAttach(string imagePath, string diskIdentifier, PartitionTableType tableType, bool readOnly = false) => throw new NotImplementedException();

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to dispose in the stub implementation
            GC.SuppressFinalize(this);
        }
    }
}
