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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend;

public static class WebModules
{
    /// <summary>
    /// The list of all built-in web modules
    /// </summary>
    public static IReadOnlyList<IWebModule> BuiltInWebModules => SupportedWebmodules;

    /// <summary>
    /// Lazy loaded list of proprietary web modules
    /// </summary>
    private static Lazy<IReadOnlyList<IWebModule>> ProprietaryWebModules = new Lazy<IReadOnlyList<IWebModule>>(() =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DUPLICATI_DISABLE_PROPRIETARY_MODULES")))
                return LoadProprietaryModules();
        }
        catch
        {
        }

        return Array.Empty<IWebModule>();
    });

    /// <summary>
    /// Loads the proprietary modules, and is marked as NoInlining to avoid JIT errors if the library is missing
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<IWebModule> LoadProprietaryModules()
    {
        return Proprietary.LoaderHelper.WebModules.LicensedWebModules.WhereNotNull().ToList();
    }

    /// <summary>
    /// Calculate list once and cache it
    /// </summary>
    private static readonly IReadOnlyList<IWebModule> SupportedWebmodules = new IWebModule[]
    {
        new GoogleServices.GCSConfig(),
        new OpenStack.SwiftConfig(),
        new S3Config(),
        new S3IAM(),
        new KeyGenerator(),
        new KeyUploader(),
        new Storj.StorjConfig(),
        new Duplicati.ListBackupsModule(),
        new Filen.GetApiKeyModule(),
    }
    .Concat(ProprietaryWebModules.Value)
    .WhereNotNull()
    .ToList();

}
