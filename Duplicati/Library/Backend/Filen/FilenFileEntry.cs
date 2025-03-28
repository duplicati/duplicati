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

namespace Duplicati.Library.Backend.Filen;

/// <summary>
/// Representation of a file or folder as returned by the Filen API
/// </summary>
public sealed record FilenFileEntry
{
    /// <summary>
    /// The UUID of the file or folder
    /// </summary>
    public required string Uuid { get; set; }
    /// <summary>
    /// The name of the file or folder
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// True if the entry is a folder, false if it is a file
    /// </summary>
    public required bool IsFolder { get; set; }
    /// <summary>
    /// The size of the file in bytes, or 0 if the entry is a folder
    /// </summary>
    public required long Size { get; set; }
    /// <summary>
    /// The last modified date of the file
    /// </summary>
    public required DateTime LastModified { get; set; }
    /// <summary>
    /// The region of the file
    /// </summary>
    public required string Region { get; set; }
    /// <summary>
    /// The bucket of the file
    /// </summary>
    public required string Bucket { get; set; }
    /// <summary>
    /// The number of chunks in the file
    /// </summary>
    public required int Chunks { get; set; }
    /// <summary>
    /// The encrypted metadata of the file
    /// </summary>
    public required string FileKey { get; set; }
    /// <summary>
    /// The encryption version
    /// </summary>
    public required int Version;
}

