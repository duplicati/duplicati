﻿//  Copyright (C) 2016, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using CoCoL;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal struct StreamProcessResult
    {
        public string Streamhash;
        public long Streamlength;
        public long Blocksetid; 
    }

    internal struct StreamBlock
    {
        public string Path;
        public Stream Stream;
        public bool IsMetadata;
        public CompressionHint Hint;
        public TaskCompletionSource<StreamProcessResult> Result;

        public static async Task<StreamProcessResult> ProcessStream(IWriteChannel<StreamBlock> channel, string path, Stream stream, bool isMetadata, CompressionHint hint)
        {
            var tcs = new TaskCompletionSource<StreamProcessResult>();
            await channel.WriteAsync(new StreamBlock()
            {
                Path = path,
                Stream = stream,
                IsMetadata = isMetadata,
                Hint = hint,
                Result = tcs
            });

            return await tcs.Task.ConfigureAwait(false);
        }
    }
}
