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

using CoCoL;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal struct StreamProcessResult
    {
        public string Streamhash { get; internal set; }
        public long Streamlength { get; internal set; }
        public long Blocksetid { get; internal set; }
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

            // limit the stream length to that found now, a fixed point in time
            var limitedStream = new Library.Utility.ReadLimitLengthStream(stream, stream.Length);
            
            var streamBlock = new StreamBlock
            {
                Path = path,
                Stream = limitedStream,
                IsMetadata = isMetadata,
                Hint = hint,
                Result = tcs
            };
            
            await channel.WriteAsync(streamBlock);
            
            return await tcs.Task.ConfigureAwait(false);
        }
    }

}
