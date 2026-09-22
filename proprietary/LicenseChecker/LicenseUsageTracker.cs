// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.LicenseChecker;

/// <summary>
/// Collects how many licensed seats the source modules consumed, per license feature, for a single run.
/// The Microsoft 365 and Google Workspace source providers record their seat counters into
/// <see cref="Current"/> as they grow. A host that runs one backup in its process, such as the managed
/// runner, installs an instance before the backup and reads it afterwards to report the seats the backup
/// used. <see cref="Current"/> is null by default, so the regular client, which runs many backups in one
/// process, records nothing and never mixes the counts of different backups.
/// </summary>
public sealed class LicenseUsageTracker
{
    /// <summary>
    /// The tracker the source providers record into, or null when no host is collecting usage
    /// </summary>
    public static LicenseUsageTracker? Current { get; set; }

    private readonly object _lock = new();
    private readonly Dictionary<string, int> _usedSeats = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records the seats consumed for a feature. The highest count reported for a feature is kept, so a
    /// provider can report its counter each time it grows.
    /// </summary>
    /// <param name="feature">The license feature, see <see cref="DuplicatiLicenseFeatures"/></param>
    /// <param name="usedSeats">The seats consumed so far</param>
    public void Record(string feature, int usedSeats)
    {
        if (string.IsNullOrWhiteSpace(feature) || usedSeats < 0)
            return;

        lock (_lock)
        {
            if (!_usedSeats.TryGetValue(feature, out var current) || usedSeats > current)
                _usedSeats[feature] = usedSeats;
        }
    }

    /// <summary>
    /// Gets the seats consumed for a feature, zero when the feature was not used
    /// </summary>
    public int GetUsedSeats(string feature)
    {
        lock (_lock)
            return _usedSeats.GetValueOrDefault(feature, 0);
    }

    /// <summary>
    /// Gets a copy of the recorded usage per feature
    /// </summary>
    public IReadOnlyDictionary<string, int> Snapshot()
    {
        lock (_lock)
            return new Dictionary<string, int>(_usedSeats, StringComparer.OrdinalIgnoreCase);
    }
}
