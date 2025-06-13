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

namespace Duplicati.Library.Backend;

/// <summary>
/// Native CIFS/SMB Backend implementation
/// </summary>
public class CIFSBackend : SMBBackend
{
    /// <summary>
    /// Log tag for the backend
    /// </summary>
    public static readonly string LOGTAG = Logging.Log.LogTagFromType<CIFSBackend>();
    /// <summary>
    /// Gets the protocol key for the backend
    /// </summary>
    public override string ProtocolKey => "cifs";

    /// <summary>
    /// Gets the display name for the backend
    /// </summary>
    public override string DisplayName => "CIFS (deprecated)";

    /// <summary>
    /// Gets the description for the backend
    /// </summary>
    public override string Description => "Same as SMB backend, but with a different name. Use SMB instead.";

    /// <summary>
    /// Empty constructor is required for the backend to be loaded by the backend factory
    /// </summary>
    public CIFSBackend() : base()
    {
    }

    /// <summary>
    /// Actual constructor for the backend that accepts the url and options
    /// </summary>
    /// <param name="url">URL in Duplicati Uri format</param>
    /// <param name="options">options to be used in the backend</param>
    public CIFSBackend(string url, Dictionary<string, string?> options)
        : base(url, options)
    {
        Logging.Log.WriteWarningMessage(LOGTAG, "DeprecatedCIFSBackend", null, "The CIFS backend is deprecated, please use the SMB backend instead.");
    }
}