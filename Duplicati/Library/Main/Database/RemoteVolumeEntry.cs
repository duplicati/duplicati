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

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// Represents a remote volume entry with its ID, name, hash, size, type, state,
    /// delete grace period, and archive time.
    /// Implements the IRemoteVolume interface.
    /// </summary>
    public struct RemoteVolumeEntry : IRemoteVolume
    {
        /// <summary>
        /// The ID of the remote volume entry.
        /// </summary>
        public long ID { get; private set; }
        public string Name { get; private set; }
        public string Hash { get; private set; }
        public long Size { get; private set; }
        /// <summary>
        /// The type of the remote volume, indicating whether it is a file or a directory.
        /// </summary>
        public RemoteVolumeType Type { get; private set; }
        /// <summary>
        /// The state of the remote volume, indicating its current status.
        /// </summary>
        public RemoteVolumeState State { get; private set; }
        /// <summary>
        /// The time period during which the remote volume can be deleted without permanent loss.
        /// </summary>
        public DateTime DeleteGracePeriod { get; private set; }
        /// <summary>
        /// The time when the remote volume was archived, indicating when it was moved to a less accessible state.
        /// </summary>
        public DateTime ArchiveTime { get; private set; }

        /// <summary>
        /// Represents an empty remote volume entry with default values.
        /// This is useful for initializing or resetting remote volume entries.
        /// </summary>
        public static readonly RemoteVolumeEntry Empty = new RemoteVolumeEntry(-1, null, null, -1, (RemoteVolumeType)(-1), (RemoteVolumeState)(-1), default(DateTime), default(DateTime));

        /// <summary>
        /// Initializes a new instance of the RemoteVolumeEntry struct with specified values.
        /// </summary>
        /// <param name="id">The ID of the remote volume entry.</param>
        /// <param name="name">The name of the remote volume entry.</param>
        /// <param name="hash">The hash of the remote volume entry.</param>
        /// <param name="size">The size of the remote volume entry.</param>
        /// <param name="type">The type of the remote volume entry (file or directory).</param>
        /// <param name="state">The state of the remote volume entry.</param>
        /// <param name="deleteGracePeriod">The delete grace period for the remote volume entry.</param>
        /// <param name="archiveTime">The archive time for the remote volume entry.</param>
        public RemoteVolumeEntry(long id, string name, string hash, long size, RemoteVolumeType type, RemoteVolumeState state, DateTime deleteGracePeriod, DateTime archiveTime)
        {
            ID = id;
            Name = name;
            Size = size;
            Type = type;
            State = state;
            Hash = hash;
            DeleteGracePeriod = deleteGracePeriod;
            ArchiveTime = archiveTime;
        }
    }
}
