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

using System.Runtime.CompilerServices;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SourceProviders;

/// <summary>
/// Shared list of statically linked source-provider modules
/// </summary>
public static class SourceProviderModules
{
    /// <summary>
    /// The list of all built-in source-provider modules
    /// </summary>
    public static IReadOnlyList<ISourceProviderModule> BuiltInSourceProviderModules => SupportedSourceProviders;

    /// <summary>
    /// Lazy loaded list of proprietary source-provider modules
    /// </summary>
    private static Lazy<IReadOnlyList<ISourceProviderModule>> ProprietarySourceProviderModules = new Lazy<IReadOnlyList<ISourceProviderModule>>(() =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DUPLICATI_DISABLE_PROPRIETARY_MODULES")))
                return LoadProprietaryModules();
        }
        catch
        {
        }

        return Array.Empty<ISourceProviderModule>();
    });

    /// <summary>
    /// Loads the proprietary modules, and is marked as NoInlining to avoid JIT errors if the library is missing
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<ISourceProviderModule> LoadProprietaryModules()
    {
        return Proprietary.LoaderHelper.SourceProviderModules.LicensedSourceProviderModules.WhereNotNull().ToList();
    }

    /// <summary>
    /// Calculate list once and cache it
    /// </summary>
    private static readonly IReadOnlyList<ISourceProviderModule> SupportedSourceProviders = new ISourceProviderModule[] {
    }
    .Concat(ProprietarySourceProviderModules.Value)
    .WhereNotNull()
    .ToList();
}

