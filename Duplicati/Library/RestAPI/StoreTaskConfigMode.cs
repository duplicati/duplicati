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
    /// Include the current job's backup configuration.
    /// When encryption is enabled, includes all secrets.
    /// When encryption is not enabled, excludes secrets.
    /// </summary>
    Self,
    /// <summary>
    /// Include all job backup configurations.
    /// When encryption is enabled, includes all secrets.
    /// When encryption is not enabled, excludes secrets.
    /// </summary>
    All,
    /// <summary>
    /// Do not include any task configuration.
    /// </summary>
    None,
    /// <summary>
    /// Include the current job's backup configuration with all secrets included.
    /// </summary>
    SelfWithForcedSecrets,
    /// <summary>
    /// Include all job backup configurations with all secrets included.
    /// </summary>
    AllWithForcedSecrets
}

