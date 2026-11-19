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

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Duplicati.Library.SourceProvider.DiskImage.Disk
{
    /// <summary>
    /// Locates the native wrapper libraries used by the raw disk implementations.
    /// The project file ships one build per platform under
    /// <c>runtimes/&lt;rid&gt;/native/</c>, which the .NET runtime does not probe
    /// for libraries that are not listed in the deps.json. This resolver probes
    /// the application folder first (packaged installs, where the release builder
    /// places the matching library) and then the runtimes folder for the current
    /// platform (source checkouts built with <c>dotnet run</c>).
    /// </summary>
    internal static class NativeWrapperResolver
    {
        /// <summary>
        /// The library name used by the Linux P/Invoke declarations
        /// </summary>
        public const string LinuxLibraryName = "libc_wrapper";

        /// <summary>
        /// The library name used by the macOS P/Invoke declarations
        /// </summary>
        public const string MacLibraryName = "libSystem_wrapper.dylib";

        /// <summary>
        /// The file names on disk, keyed by the P/Invoke library name
        /// </summary>
        private static readonly Dictionary<string, string> FileNames = new(StringComparer.Ordinal)
        {
            { LinuxLibraryName, "libc_wrapper.so" },
            { MacLibraryName, "libSystem_wrapper.dylib" },
        };

        /// <summary>
        /// Lock guarding the registration
        /// </summary>
        private static readonly object m_lock = new();

        /// <summary>
        /// Flag indicating if the resolver has been registered
        /// </summary>
        private static bool m_registered;

        /// <summary>
        /// Registers the resolver for the assembly containing this class.
        /// Safe to call multiple times.
        /// </summary>
        public static void EnsureRegistered()
        {
            lock (m_lock)
            {
                if (m_registered)
                    return;

                NativeLibrary.SetDllImportResolver(typeof(NativeWrapperResolver).Assembly, Resolve);
                m_registered = true;
            }
        }

        /// <summary>
        /// The runtimes folder name for the current platform, or null if not supported
        /// </summary>
        internal static string? CurrentRuntimeFolder
        {
            get
            {
                if (OperatingSystem.IsMacOS())
                    return "osx";

                if (OperatingSystem.IsLinux())
                    return RuntimeInformation.ProcessArchitecture switch
                    {
                        Architecture.X86 => "linux-x86",
                        Architecture.X64 => "linux-x64",
                        Architecture.Arm => "linux-arm",
                        Architecture.Arm64 => "linux-arm64",
                        _ => null
                    };

                return null;
            }
        }

        /// <summary>
        /// Returns the candidate paths for a wrapper library, in probing order
        /// </summary>
        /// <param name="libraryName">The P/Invoke library name</param>
        /// <param name="assembly">The assembly requesting the library</param>
        /// <returns>The candidate paths, empty if the name is not a wrapper library</returns>
        internal static IEnumerable<string> GetCandidatePaths(string libraryName, Assembly assembly)
        {
            if (!FileNames.TryGetValue(libraryName, out var fileName))
                yield break;

            var folders = new List<string>();
            var assemblyDir = string.IsNullOrEmpty(assembly.Location) ? null : Path.GetDirectoryName(assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
                folders.Add(assemblyDir);
            if (!string.IsNullOrEmpty(AppContext.BaseDirectory) && !string.Equals(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), assemblyDir, StringComparison.Ordinal))
                folders.Add(AppContext.BaseDirectory);

            var rid = CurrentRuntimeFolder;
            foreach (var folder in folders)
            {
                // Packaged layout: the release builder places the library next to the assemblies
                yield return Path.Combine(folder, fileName);

                // Build output layout: all platforms are present under runtimes/
                if (rid != null)
                    yield return Path.Combine(folder, "runtimes", rid, "native", fileName);
            }
        }

        /// <summary>
        /// The resolver callback invoked by the runtime for each P/Invoke library in the assembly
        /// </summary>
        /// <param name="libraryName">The name requested by the P/Invoke declaration</param>
        /// <param name="assembly">The assembly requesting the library</param>
        /// <param name="searchPath">The search path flags</param>
        /// <returns>The library handle, or zero to fall back to the default probing</returns>
        private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            foreach (var candidate in GetCandidatePaths(libraryName, assembly))
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                    return handle;

            // Fall back to the default probing, which produces the standard error message
            return nint.Zero;
        }
    }
}
