// Copyright (C) 2024, The Duplicati Team
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
using DeviceId;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Helper class to get the device ID string and computed hash
/// </summary>
public static class DeviceIDHelper
{
    /// <summary>
    /// Generates an empty ID
    /// </summary>
    /// <returns>An empty ID</returns>
    private static string? GenerateEmptyId()
    {
        var builder = new DeviceIdBuilder();
        builder.Components.Clear();
        return builder.ToString();
    }

    /// <summary>
    /// The empty ID produced by DeviceIdBuilder if no information is available
    /// </summary>
    private static readonly HashSet<string> EMPTY_DEVICE_IDS = new[] {
        // Known empty Id
        "WERC8GMRZGE196QVYK49JVXS4GKTWGF4CJDS6K54JPCHPY2JQ1AG",
        // In case the library changes
        GenerateEmptyId()
    }
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => x!)
    // Don't allow empty strings either
    .Append(string.Empty)
    .ToHashSet();

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
    /// Returns a configured builder
    /// </summary>
    /// <returns>The builder</returns>
    private static DeviceIdBuilder GetBuilder()
        => new DeviceIdBuilder()
            .OnWindows(deviceid => deviceid
                .AddMotherboardSerialNumber())
            .OnLinux(deviceid => deviceid
                .AddMotherboardSerialNumber())
            .OnMac(deviceid => deviceid
                .AddPlatformSerialNumber());

    /// <summary>
    /// Get the device ID string (motherboard serial number on Windows and Linux, platform serial number on Mac)
    /// </summary>
    /// <returns>The device Id</returns>
    public static string? GetDeviceID()
    {
        var id = GetBuilder().ToString();
        if (string.IsNullOrWhiteSpace(id))
            return EMPTY_DEVICE_IDS.First();

        return id;
    }

    /// <summary>
    /// Checks if the deviceId is a meaningful value
    /// </summary>
    /// <returns><c>true</c> if a deviceId can be obtained, <c>false</c> otherwise</returns>
    public static bool CanGetDeviceId()
    {
        var builder = GetBuilder();
        builder.UseFormatter(new DeviceId.Formatters.StringDeviceIdFormatter(new DeviceId.Encoders.PlainTextDeviceIdComponentEncoder()));
        return !string.IsNullOrWhiteSpace(builder.ToString());
    }

    /// <summary>
    /// Get the device ID hashed in SHA256 and in hex format 
    /// </summary>
    /// <returns></returns>
    public static string GetDeviceIDHash()
    {
        using var hasher = HashFactory.CreateHasher("SHA256");
        return (GetDeviceID() ?? "").ComputeHashToHex(hasher);
    }
}