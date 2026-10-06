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

using System.Security.Cryptography;
using System.Text;

namespace Duplicati.WebserverCore.Middlewares;

/// <summary>
/// A per-process secret that marks requests the server sends to itself, such as forwarded remote control commands.
/// </summary>
/// <remarks>
/// The NAS integrated authentication middlewares (Synology DSM, QNAP QTS) require a NAS login session,
/// which requests originating from inside the process cannot have. Such requests carry this key instead,
/// which lets them skip the NAS session check. The key never leaves the process, so it cannot be supplied
/// by an external client, and the request is still subject to the regular Duplicati authentication.
/// </remarks>
public static class LocalForwardingKey
{
    /// <summary>
    /// The name of the header carrying the key
    /// </summary>
    public const string HeaderName = "X-Duplicati-LocalForwardingKey";

    /// <summary>
    /// The key for this process instance
    /// </summary>
    public static readonly string Key;

    /// <summary>
    /// The key as bytes, for fixed-time comparison
    /// </summary>
    private static readonly byte[] KeyBytes;

    /// <summary>
    /// Generates the key for this process instance
    /// </summary>
    static LocalForwardingKey()
    {
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        KeyBytes = Encoding.UTF8.GetBytes(Key);
    }

    /// <summary>
    /// Checks if the request carries the key for this process instance.
    /// </summary>
    /// <param name="request">The request to check</param>
    /// <returns><c>true</c> if the request originates from this process; <c>false</c> otherwise</returns>
    public static bool IsLocalForwardedRequest(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1)
            return false;

        var value = values[0];
        if (string.IsNullOrEmpty(value))
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), KeyBytes);
    }
}
