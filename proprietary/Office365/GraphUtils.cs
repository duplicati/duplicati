// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Globalization;
using System.Text.Json;

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

    public static string ToGraphTimeString(this DateTime dt)
        => dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string ToGraphTimeString(this DateTimeOffset dto)
        => dto.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    internal static DateTimeOffset? GetStartUtcHint(this GraphEvent ev)
    {
        if (ev.OriginalStart.HasValue)
            return ev.OriginalStart.Value;

        var dtz = AsDateTimeTimeZone(ev.Start);
        if (dtz?.DateTime is null)
            return null;

        // Graph dateTimeTimeZone.dateTime is typically offset-less.
        if (!DateTime.TryParse(
                dtz.DateTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            // Try strict round-trip if it happens to include offset
            if (!DateTimeOffset.TryParse(dtz.DateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                return null;
            return dto.ToUniversalTime();
        }

        // Treat as UTC hint; we will query a wider range to compensate.
        return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
    }

    private static GraphDateTimeTimeZone? AsDateTimeTimeZone(object? value)
    {
        if (value is null)
            return null;

        if (value is GraphDateTimeTimeZone g)
            return g;

        if (value is JsonElement je)
        {
            // Deserialize from JsonElement without allocating big intermediates
            try { return je.Deserialize<GraphDateTimeTimeZone>(); }
            catch { return null; }
        }

        // Some serializers may give JsonDocument or boxed dictionaries; fallback:
        if (value is JsonDocument jd)
        {
            try { return jd.RootElement.Deserialize<GraphDateTimeTimeZone>(); }
            catch { return null; }
        }

        return null;
    }

}
