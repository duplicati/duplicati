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

#nullable enable

using System;

namespace Duplicati.Server;

/// <summary>
/// Defines how the task configuration should be stored in the backup.
/// </summary>
public enum StoreTaskConfigMode
{
    /// <summary>
    /// Automatically determine behavior based on encryption settings.
    /// When encryption is enabled, behaves as <see cref="Self"/>.
    /// When encryption is not enabled, behaves as <see cref="None"/>.
    /// </summary>
    Auto,
    /// <summary>
    /// Do not include any task configuration.
    /// </summary>
    None,
    /// <summary>
    /// Include the current job's backup configuration without secrets.
    /// </summary>
    Self,
    /// <summary>
    /// Include all job backup configurations without secrets.
    /// </summary>
    All,
    /// <summary>
    /// Include the current job's backup configuration with all secrets included.
    /// </summary>
    SelfWithSecrets,
    /// <summary>
    /// Include all job backup configurations with all secrets included.
    /// </summary>
    AllWithSecrets,
    /// <summary>
    /// Include the current job's backup configuration with all secrets included, even if encryption is disabled.
    /// </summary>
    SelfWithUnencryptedSecrets,
    /// <summary>
    /// Include all job backup configurations with all secrets included, even if encryption is disabled.
    /// </summary>
    AllWithUnencryptedSecrets,
}

/// <summary>
/// The resolved mode for storing task configuration.
/// </summary>
/// <param name="IncludeAllTasks">A value indicating whether to include all tasks.</param>
/// <param name="RemoveSecrets">A value indicating whether to remove secrets.</param>
public record ResolvedTaskConfigMode(
    bool IncludeAllTasks,
    bool RemoveSecrets
);

/// <summary>
/// Extension methods for <see cref="StoreTaskConfigMode"/>.
/// </summary>
public static class StoreTaskConfigModeExtensions
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType(typeof(StoreTaskConfigModeExtensions));

    /// <summary>
    /// Resolves the effective mode based on the current mode and encryption settings.
    /// </summary>
    /// <param name="mode">The mode to resolve.</param>
    /// <param name="encryptionEnabled">A flag indicating whether encryption is enabled.</param>
    /// <returns>The effective mode</returns>
    public static ResolvedTaskConfigMode? ResolvedTaskConfigMode(this StoreTaskConfigMode mode, bool encryptionEnabled)
    {
        var effectiveMode = mode switch
        {
            StoreTaskConfigMode.Auto => encryptionEnabled ? StoreTaskConfigMode.Self : StoreTaskConfigMode.None,
            _ => mode
        };

        if (!encryptionEnabled && effectiveMode is StoreTaskConfigMode.SelfWithSecrets or StoreTaskConfigMode.AllWithSecrets)
        {
            if (effectiveMode is StoreTaskConfigMode.SelfWithSecrets)
            {
                effectiveMode = StoreTaskConfigMode.Self;
                Library.Logging.Log.WriteWarningMessage(LOGTAG, "NotStoringUnencryptedSecrets", null, $"Refusing to store secrets in an unencrypted backup, reverting to {effectiveMode}");
            }
            else if (effectiveMode is StoreTaskConfigMode.AllWithSecrets)
            {
                effectiveMode = StoreTaskConfigMode.All;
                Library.Logging.Log.WriteWarningMessage(LOGTAG, "NotStoringUnencryptedSecrets", null, $"Refusing to store secrets in an unencrypted backup, reverting to {effectiveMode}");
            }
        }

        if (effectiveMode == StoreTaskConfigMode.None)
            return null;
        if (effectiveMode == StoreTaskConfigMode.Auto)
            throw new InvalidOperationException("Auto mode should have been resolved to a concrete mode");

        var removeSecrets = effectiveMode.RemoveSecrets();
        var includeAllTasks = effectiveMode.IncludeAllTasks();

        // Sanity check: if encryption is disabled, we should not be storing secrets. Effectively unreachable.
        if (!encryptionEnabled && !removeSecrets)
        {
            if (!(effectiveMode is StoreTaskConfigMode.SelfWithUnencryptedSecrets or StoreTaskConfigMode.AllWithUnencryptedSecrets))
                throw new InvalidOperationException($"Mode {effectiveMode} is not allowed when encryption is disabled");
        }

        return new ResolvedTaskConfigMode(
            includeAllTasks,
            removeSecrets
        );
    }

    /// <summary>
    /// Determines whether secrets should be removed based on the current mode.
    /// </summary>
    /// <param name="mode">The mode to check.</param>
    /// <returns><c>true</c> if secrets should be removed; otherwise, <c>false</c>.</returns>
    private static bool RemoveSecrets(this StoreTaskConfigMode mode)
        => mode switch
        {
            StoreTaskConfigMode.SelfWithSecrets
            or StoreTaskConfigMode.AllWithSecrets
            or StoreTaskConfigMode.SelfWithUnencryptedSecrets
            or StoreTaskConfigMode.AllWithUnencryptedSecrets => false,
            _ => true
        };

    /// <summary>
    /// Determines whether all tasks should be included based on the current mode.
    /// </summary>
    /// <param name="mode">The mode to check.</param>
    /// <returns><c>true</c> if all tasks should be included; otherwise, <c>false</c>.</returns>
    private static bool IncludeAllTasks(this StoreTaskConfigMode mode)
        => mode switch
        {
            StoreTaskConfigMode.All or StoreTaskConfigMode.AllWithSecrets or StoreTaskConfigMode.AllWithUnencryptedSecrets => true,
            _ => false
        };
}

