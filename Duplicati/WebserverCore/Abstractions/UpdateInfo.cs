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
using Duplicati.Library.AutoUpdater;

namespace Duplicati.WebserverCore.Abstractions;

public class UpdateInfo
{
    public required int MinimumCompatibleVersion { get; init; }
    public required string? IncompatibleUpdateUrl { get; init; }
    public required string? Displayname { get; init; }
    public required string? Version { get; init; }
    public required DateTime ReleaseTime { get; init; }
    public required string? ReleaseType { get; init; }
    public required string? UpdateSeverity { get; init; }
    public required string? ChangeInfo { get; init; }
    public required int PackageUpdaterVersion { get; init; }
    public required PackageEntry[]? Packages { get; init; }
    public required string GenericUpdatePageUrl { get; init; }

    public static UpdateInfo? FromSrc(Library.AutoUpdater.UpdateInfo? src)
    {
        if (src == null)
            return null;

        return new UpdateInfo
        {
            MinimumCompatibleVersion = src.MinimumCompatibleVersion,
            IncompatibleUpdateUrl = src.IncompatibleUpdateUrl,
            Displayname = src.Displayname,
            Version = src.Version,
            ReleaseTime = src.ReleaseTime,
            ReleaseType = src.ReleaseType,
            UpdateSeverity = src.UpdateSeverity,
            ChangeInfo = src.ChangeInfo,
            PackageUpdaterVersion = src.PackageUpdaterVersion,
            Packages = src.Packages,
            GenericUpdatePageUrl = src.GenericUpdatePageUrl
        };
    }
}