// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365;

public static class GraphUtils
{
    public static DateTime FromGraphDateTime(this DateTimeOffset? dto)
        => dto?.UtcDateTime ?? DateTime.UnixEpoch;

    public static string SanitizePathComponent(string pathComponent)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(pathComponent.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        return sanitized;
    }
}
