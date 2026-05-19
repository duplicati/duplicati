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

namespace Duplicati.Library.Backend.DrimeCloud.Model;

/// <summary>
/// Authentication response from Drime Cloud API
/// </summary>
public class AuthResponse
{
    /// <summary>
    /// Response status (success/error)
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Error message if authentication failed
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// User information including access token
    /// </summary>
    public AuthUser? User { get; set; }
}

/// <summary>
/// User information returned by authentication
/// </summary>
public class AuthUser
{
    /// <summary>
    /// User ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// User email address
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// User display name
    /// </summary>
    public string Display_Name { get; set; } = string.Empty;

    /// <summary>
    /// First name
    /// </summary>
    public string First_Name { get; set; } = string.Empty;

    /// <summary>
    /// Last name
    /// </summary>
    public string Last_Name { get; set; } = string.Empty;

    /// <summary>
    /// Access token for API authentication
    /// </summary>
    public string Access_Token { get; set; } = string.Empty;

    /// <summary>
    /// Account creation timestamp
    /// </summary>
    public string Created_At { get; set; } = string.Empty;

    /// <summary>
    /// Account last update timestamp
    /// </summary>
    public string Updated_At { get; set; } = string.Empty;

    /// <summary>
    /// Ban timestamp if user is banned
    /// </summary>
    public string? Banned_At { get; set; }
}
