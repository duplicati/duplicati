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
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Database
{
    public struct RemoteVolumeEntry : IRemoteVolume
    {
        public long ID { get; private set; }
        public string Name { get; private set; }
        public string Hash { get; private set; }
        public long Size { get; private set; }
        public RemoteVolumeType Type { get; private set; }
        public RemoteVolumeState State { get; private set; }
        public DateTime DeleteGracePeriod { get; private set; }

        public static readonly RemoteVolumeEntry Empty = new RemoteVolumeEntry(-1, null, null, -1, (RemoteVolumeType)(-1), (RemoteVolumeState)(-1), default(DateTime));

        public RemoteVolumeEntry(long id, string name, string hash, long size, RemoteVolumeType type, RemoteVolumeState state, DateTime deleteGracePeriod)
        {
            ID = id;
            Name = name;
            Size = size;
            Type = type;
            State = state;
            Hash = hash;
            DeleteGracePeriod = deleteGracePeriod;
        }
    }
}
