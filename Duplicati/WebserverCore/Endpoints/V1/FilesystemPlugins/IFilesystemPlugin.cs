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
namespace Duplicati.WebserverCore.Endpoints.V1.FilesystemPlugins;

/// <summary>
/// Plugin for adding items to the filesystem tree.
/// </summary>
public interface IFilesystemPlugin
{
    /// <summary>
    /// The root key for this plugin, which will be used to identify it in the filesystem tree.
    /// </summary>
    public string RootName { get; }
    /// <summary>
    /// Retrieves the folders for the given path segments.
    /// </summary>
    /// <param name="pathSegments">The path segments to retrieve folders for.</param>
    /// <returns>The list of entries as <see cref="Dto.TreeNodeDto"/> objects.</returns>
    public IEnumerable<Dto.TreeNodeDto> GetEntries(string[] pathSegments);
}

/// <summary>
/// Helper class to get all filesystem plugins.
/// </summary>
public static class KnownPlugins
{
    /// <summary>
    /// Gets all filesystem plugins.
    /// </summary>
    /// <returns>An enumerable of <see cref="IFilesystemPlugin"/> instances.</returns>
    public static IEnumerable<IFilesystemPlugin> GetPlugins()
    {
        yield return new Hyperv();
        yield return new MSSQL();
    }
}
