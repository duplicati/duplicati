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
#nullable enable

using Duplicati.Library.Utility;
using System.Collections.Generic;
using System.Linq;
using System;

/// <summary>
/// Helper class to get the device ID string and computed hash
/// </summary>
public static class DeviceIDHelper
{

    /// <summary>
    /// The empty ID produced by DeviceIdBuilder if no information is available
    /// </summary>
    private static readonly string[] EMPTY_DEVICE_IDS = [
        // Known empty Id
        "WERC8GMRZGE196QVYK49JVXS4GKTWGF4CJDS6K54JPCHPY2JQ1AG",
        string.Empty
    ];

    /// <summary>
    /// Hashes a set of device IDs
    /// </summary>
    /// <param name="deviceIds">The deviceIds to hash</param>
    /// <returns>The list of hashed ids</returns>
    private static HashSet<string> HashDeviceIds(IEnumerable<string> deviceIds)
    {
        var hasher = HashFactory.CreateHasher("SHA256");
        return deviceIds.Select(x => x.ComputeHashToHex(hasher)).ToHashSet();
    }

    /// <summary>
    /// The empty deviceId hashes
    /// </summary>
    public static readonly HashSet<string> EMPTY_DEVICE_ID_HASHES = HashDeviceIds(EMPTY_DEVICE_IDS);

    /// <summary>
    /// Get the device ID hashed in SHA256 and in hex format 
    /// </summary>
    /// <returns></returns>
    public static string GetDeviceIDHash()
        => throw new InvalidOperationException("Device ID is not available");


    /// <summary>
    /// Returns a value indicating if the device ID is available on this system
    /// </summary>
    public static bool HasTrustedDeviceID => false;
}