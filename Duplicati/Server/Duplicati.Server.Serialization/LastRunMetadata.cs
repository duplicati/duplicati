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
using System.Collections.Generic;

#nullable enable

namespace Duplicati.Server.Serialization
{
    /// <summary>
    /// Reads the last completed run from the metadata of a backup configuration.
    /// A run is recorded under keys named after the operation it performed, so the
    /// keys to read depend on the operation type of the configuration.
    /// </summary>
    public static class LastRunMetadata
    {
        /// <summary>
        /// Gets the metadata key holding the time the last completed run started
        /// </summary>
        /// <param name="operationType">The operation type of the configuration</param>
        /// <returns>The metadata key</returns>
        public static string StartedKey(OperationType operationType)
            => operationType == OperationType.Sync ? "LastSyncStarted" : "LastBackupStarted";

        /// <summary>
        /// Gets the metadata key holding the time the last completed run finished
        /// </summary>
        /// <param name="operationType">The operation type of the configuration</param>
        /// <returns>The metadata key</returns>
        public static string FinishedKey(OperationType operationType)
            => operationType == OperationType.Sync ? "LastSyncFinished" : "LastBackupFinished";

        /// <summary>
        /// Gets the metadata key holding the duration of the last completed run
        /// </summary>
        /// <param name="operationType">The operation type of the configuration</param>
        /// <returns>The metadata key</returns>
        public static string DurationKey(OperationType operationType)
            => operationType == OperationType.Sync ? "LastSyncDuration" : "LastBackupDuration";

        /// <summary>
        /// Gets the serialized time the last completed run started
        /// </summary>
        /// <param name="metadata">The metadata, or null if none</param>
        /// <param name="operationType">The operation type of the configuration</param>
        /// <returns>The serialized value, or null if there is no completed run</returns>
        public static string? GetStarted(IDictionary<string, string>? metadata, OperationType operationType)
            => Read(metadata, StartedKey(operationType));

        /// <summary>
        /// Reads a non-empty value from the metadata
        /// </summary>
        private static string? Read(IDictionary<string, string>? metadata, string key)
            => metadata != null && metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
    }
}
