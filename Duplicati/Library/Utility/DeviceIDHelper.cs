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

using Duplicati.Library.Utility;
using DeviceId;

/// <summary>
/// Helper class to get the device ID string and computed hash
/// </summary>
public static class DeviceIDHelper
{

    /// <summary>
    /// Get the device ID string (motherboard serial number on Windows and Linux, platform serial number on Mac)
    /// </summary>
    /// <returns>String</returns>
    public static string GetDeviceID()
    {
        return new DeviceIdBuilder()
        .OnWindows(deviceid => deviceid
            .AddMotherboardSerialNumber())
        .OnLinux(deviceid => deviceid
            .AddMotherboardSerialNumber())
        .OnMac(deviceid => deviceid
            .AddPlatformSerialNumber())
        .ToString();
    }

    /// <summary>
    /// Get the device ID hashed in SHA256 and in hex format 
    /// </summary>
    /// <returns></returns>
    public static string GetDeviceIDHash()
    {
        return GetDeviceID().ComputeHashToHex(HashFactory.CreateHasher("SHA256"));
    }
}