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

using SMBLibrary;

namespace Duplicati.Library.Backend.CIFS.Model;

/// <summary>
/// Connection parameters for establishing an SMB connection.
/// </summary>
/// <param name="ServerName">The name or IP address of the SMB server</param>
/// <param name="TransportType">The transport protocol type used for SMB communication</param>
/// <param name="ShareName">The name of the network share to connect to</param>
/// <param name="Path">The path within the share to access</param>
/// <param name="AuthDomain">The authentication domain name</param>
/// <param name="AuthUser">The username for authentication</param>
/// <param name="AuthPassword">The password for authentication</param>
/// <param name="ReadBufferSize">Read buffer size for SMB operations (will be capped automatically by SMB negotiated values)</param>
/// <param name="WriteBufferSize">Write buffer size for SMB operations (will be capped automatically by SMB negotiated values)</param>
public sealed record SMBConnectionParameters(
    string ServerName,
    SMBTransportType TransportType,
    string ShareName,
    string Path,
    string? AuthDomain,
    string? AuthUser,
    string? AuthPassword,
    int? ReadBufferSize,
    int? WriteBufferSize);