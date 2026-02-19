// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.LicenseChecker;

/// <summary>
/// The license data
/// </summary>
public record LicenseData
{
    /// <summary>
    /// The grace period after license expiration during which the software will still operate
    /// </summary>
    private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(30);

    /// <summary>
    /// When the license is valid from
    /// </summary>
    public required DateTimeOffset ValidFrom { get; init; }
    /// <summary>
    /// When the license is valid to
    /// </summary>
    public required DateTimeOffset ValidTo { get; init; }
    /// <summary>
    /// The features enabled for this license
    /// </summary>
    public required Dictionary<string, string> Features { get; init; } = new();
    /// <summary>
    /// The maximum number of machines allowed for this license
    /// </summary>
    public required int MaxMachines { get; init; }
    /// <summary>
    /// The organization ID associated with the license
    /// </summary>
    public required string OrganizationId { get; init; }
    /// <summary>
    /// The license ID
    /// </summary>
    public required string LicenseId { get; init; }
    /// <summary>
    /// When the license was generated
    /// </summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// Determines if the license is currently valid
    /// </summary>
    public bool IsValidNow => DateTimeOffset.UtcNow >= ValidFrom && DateTimeOffset.UtcNow <= ValidTo;

    /// <summary>
    /// The expiration date including the grace period
    /// </summary>
    public DateTimeOffset ValidToWithGrace => ValidTo.Add(GracePeriod);

    /// <summary>
    /// Determines if the license is in its grace period
    /// </summary>
    public bool IsInGracePeriod => DateTimeOffset.UtcNow > ValidTo && DateTimeOffset.UtcNow <= ValidToWithGrace;
}