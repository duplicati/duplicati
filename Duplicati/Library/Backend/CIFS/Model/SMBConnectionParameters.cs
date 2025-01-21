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
public record SMBConnectionParameters
{
   /// <summary>
   /// The name or IP address of the SMB server
   /// </summary>
   public string ServerName { get; init; }

   /// <summary>
   /// The transport protocol type used for SMB communication
   /// </summary>
   public SMBTransportType TransportType { get; init; }

   /// <summary>
   /// The name of the network share to connect to
   /// </summary>
   public string ShareName { get; init; }

   /// <summary>
   /// The path within the share to access
   /// </summary>
   public string Path { get; init; }

   /// <summary>
   /// The authentication domain name
   /// </summary>
   public string AuthDomain { get; init; }

   /// <summary>
   /// The username for authentication
   /// </summary>
   public string AuthUser { get; init; }

   /// <summary>
   /// The password for authentication
   /// </summary>
   public string AuthPassword { get; init; }
   
   /// <summary>
   /// Write buffer size for SMB operations (will be capped automatically by SMB negotiated values)
   /// </summary>
   public int? WriteBufferSize { get; init; }
   
   /// <summary>
   /// Read buffer size for SMB operations (will be capped automatically by SMB negotiated values)
   /// </summary>
   public int? ReadBufferSize { get; init; }

   /// <summary>
   /// Creates a new instance of SMB connection parameters
   /// </summary>
   /// <param name="serverName">The name or IP address of the SMB server</param>
   /// <param name="transportType">The transport protocol type used for SMB communication</param>
   /// <param name="shareName">The name of the network share to connect to</param>
   /// <param name="path">The path within the share to access</param>
   /// <param name="authDomain">The authentication domain name</param>
   /// <param name="authUser">The username for authentication</param>
   /// <param name="authPassword">The password for authentication</param>
   /// <param name="readBufferSize">Read buffer size for SMB operations (will be capped automatically by SMB negotiated values)</param>
   /// <param name="writeBufferSize">Write buffer size for SMB operations (will be capped automatically by SMB negotiated values)</param>
   public SMBConnectionParameters(
       string serverName,
       SMBTransportType transportType,
       string shareName,
       string path,
       string authDomain,
       string authUser,
       string authPassword,
       int? readBufferSize,
       int? writeBufferSize)
   {
       ServerName = serverName;
       TransportType = transportType;
       ShareName = shareName;
       Path = path;
       AuthDomain = authDomain;
       AuthUser = authUser;
       AuthPassword = authPassword;
       ReadBufferSize = readBufferSize;
       WriteBufferSize = writeBufferSize;
   }
}