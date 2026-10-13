// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// Extracts the token from the delta links Graph returns. Only the token is stored between
/// backups, not the whole link; the request it belongs to is rebuilt from the state key.
/// </summary>
internal static class DeltaToken
{
    /// <summary>
    /// The query parameter carrying the token in a mail delta link
    /// </summary>
    private const string MAIL_TOKEN_PARAMETER = "$deltatoken";

    /// <summary>
    /// The query parameter carrying the token in a drive delta link
    /// </summary>
    private const string DRIVE_TOKEN_PARAMETER = "token";

    /// <summary>
    /// Extracts the token from a mail delta link
    /// </summary>
    /// <param name="deltaLink">The delta link</param>
    /// <returns>The token, or <c>null</c> if the link carries none</returns>
    public static string? FromMailDeltaLink(string? deltaLink)
        => Nonempty(APIHelper.TryGetQueryValue(deltaLink ?? string.Empty, MAIL_TOKEN_PARAMETER));

    /// <summary>
    /// Extracts the token from a drive delta link
    /// </summary>
    /// <param name="deltaLink">The delta link</param>
    /// <returns>The token, or <c>null</c> if the link carries none</returns>
    public static string? FromDriveDeltaLink(string? deltaLink)
        => Nonempty(APIHelper.TryGetQueryValue(deltaLink ?? string.Empty, DRIVE_TOKEN_PARAMETER))
            ?? Nonempty(APIHelper.TryGetQueryValue(deltaLink ?? string.Empty, MAIL_TOKEN_PARAMETER));

    /// <summary>
    /// Maps an empty value to <c>null</c>
    /// </summary>
    /// <param name="value">The value</param>
    /// <returns>The value, or <c>null</c> if it is empty</returns>
    private static string? Nonempty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
