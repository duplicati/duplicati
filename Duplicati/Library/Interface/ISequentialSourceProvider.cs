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

#nullable enable

namespace Duplicati.Library.Interface;

/// <summary>
/// Marks a source provider whose entries come from a single stream that can only be read
/// forward, such as the piped output of a dump tool. Each entry can be opened once, and only
/// in the order the provider yields them. A backup with such a source reads one entry at a
/// time, whatever the configured number of concurrent file processors is.
/// </summary>
public interface ISequentialSourceProvider : ISourceProvider
{
}

/// <summary>
/// Marks a folder entry of an <see cref="ISequentialSourceProvider"/> whose children must
/// reach the backup in the order the folder yields them.
/// </summary>
public interface ISequentialSourceProviderEntry : ISourceProviderEntry
{
}

/// <summary>
/// Marks a source stream whose length is not known before it has been read to the end.
/// The backup records the number of bytes read as the size. If such a stream fails after
/// the read has started, the entry of the previous backup is kept under its name, and the
/// data that was read is stored under the name with <see cref="StreamedSource.PartialEntrySuffix"/> added.
/// </summary>
public interface IStreamedSourceStream
{
}

/// <summary>
/// Constants for sources that are read as streams
/// </summary>
public static class StreamedSource
{
    /// <summary>
    /// The suffix added to the name of an entry that holds the data read from a streamed
    /// source before the stream failed
    /// </summary>
    public const string PartialEntrySuffix = ".duplicati-partial";
}
