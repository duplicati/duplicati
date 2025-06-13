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

namespace Duplicati.WebserverCore.Abstractions;

public interface ISettingsService
{
    /// <summary>
    /// Gets the current server settings.
    /// </summary>
    /// <returns>The current server settings.</returns>
    ServerSettings GetSettings();

    /// <summary>
    /// Gets the current server settings with sensitive information masked.
    /// </summary>
    /// <returns>A dictionary of settings with sensitive information masked.</returns>
    Dictionary<string, string> GetSettingsMasked();

    /// <summary>
    /// Patches the server settings with the provided values, ignoring any sensitive information.
    /// </summary>
    /// <param name="values">A dictionary of values to patch the settings with.</param>
    void PatchSettingsMasked(Dictionary<string, object?>? values);

    /// <summary>
    /// Gets a specific setting value, masking sensitive information if necessary.
    /// </summary>
    /// <param name="key">The key of the setting to retrieve.</param>
    /// <returns>The value of the setting, or null if not found.</returns>
    string? GetSettingMasked(string key);

    /// <summary>
    /// Patches a specific setting with a masked value, ignoring any sensitive information.
    /// </summary>
    /// <param name="key">The key of the setting to patch.</param>
    /// <param name="value">The masked value to set for the setting.</param>
    void PatchSettingMasked(string key, string value);
}