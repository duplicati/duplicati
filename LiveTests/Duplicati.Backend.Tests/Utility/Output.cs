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

namespace Duplicati.Backend.Tests.Utility;
public class OutputConsumer : IOutputConsumer
{
    private readonly MemoryStream _stdout = new();
    private readonly MemoryStream _stderr = new();

    public bool Enabled => true;

    public Stream Stdout => _stdout;

    public Stream Stderr => _stderr;

    public void Dispose()
    {
        _stdout.Dispose();
        _stderr.Dispose();
    }

    public async Task<string> GetStreamsOutput()
    {
        var output = new StringBuilder();

        _stdout.Position = 0;
        using (var reader = new StreamReader(_stdout, leaveOpen: true))
        {
            output.AppendLine("STDOUT:");
            output.AppendLine(await reader.ReadToEndAsync());
        }

        _stderr.Position = 0;
        using (var reader = new StreamReader(_stderr, leaveOpen: true))
        {
            output.AppendLine("STDERR:");
            output.AppendLine(await reader.ReadToEndAsync());
        }

        return output.ToString();
    }
}