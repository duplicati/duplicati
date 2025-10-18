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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Snapshots.Windows;

/// <summary>
/// Class for implementing runtime-loading of Windows-only components
/// </summary>
public static class WindowsShimLoader
{
    /// <summary>
    /// The lock for thread-safe access to the type dictionary
    /// </summary>
    private static readonly object _loadedTypesLock = new object();

    /// <summary>
    /// Cache of types already loaded
    /// </summary>
    private static readonly Dictionary<string, Type> _loadedTypes = new Dictionary<string, Type>();

    /// <summary>
    /// Cached reference to the assembly we are loading from
    /// </summary>
    private static Assembly? _loadedAssembly;

    /// <summary>
    /// Custom load context to deal with assemblies not being listed in deps.json
    /// </summary>
    private sealed class ModulesLoadContext : AssemblyLoadContext
    {
        /// <summary>
        /// The resolver
        /// </summary>
        private readonly AssemblyDependencyResolver _resolver;

        /// <summary>
        /// Assemblies that are already loading in the main context
        /// </summary>
        private readonly HashSet<string> _knownAssemblies;

        /// <summary>
        /// Creates a new custom loading context
        /// </summary>
        /// <param name="mainAssemblyPath">The path to the assembly to load</param>
        public ModulesLoadContext(string mainAssemblyPath)
            : base("WinModules", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
            _knownAssemblies = new HashSet<string>(
                      AppDomain.CurrentDomain.GetAssemblies()
                              .Select(a => a.GetName().Name)
                              .WhereNotNull(),
                      StringComparer.OrdinalIgnoreCase); ;
        }

        /// <inheritdoc/>
        protected override Assembly? Load(AssemblyName name)
        {
            // Use existing assembly
            if (name.Name != null && _knownAssemblies.Contains(name.Name))
                return null;

            var path = _resolver.ResolveAssemblyToPath(name);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        /// <inheritdoc/>
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }

    /// <summary>
    /// Loads a type using reflection
    /// </summary>
    /// <typeparam name="T">The base type or interface to cast the result to</typeparam>
    /// <param name="classname">The name of the class to load</param>
    /// <param name="args">Any constructor arguments</param>
    /// <returns>The loaded type</returns>
    private static T LoadWithReflection<T>(string classname, params object[] args) where T : class
    {
        const string AssemblyName = "Duplicati.Library.WindowsModules";
        const string Namespace = AssemblyName;
        try
        {
            Type? type;
            lock (_loadedTypesLock)
                type = _loadedTypes.GetValueOrDefault(classname, null!);

            if (type == null)
            {
                // Lock while loading assembly so we only load once
                lock (_loadedTypesLock)
                {
                    if (_loadedAssembly == null)
                    {
                        var path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", AssemblyName + ".dll");
                        var alc = new ModulesLoadContext(path);
                        _loadedAssembly = alc.LoadFromAssemblyPath(path);
                    }
                }

                type = _loadedAssembly.GetType($"{Namespace}.{classname}", throwOnError: true, ignoreCase: false)
                    ?? throw new ArgumentException($"Failed to load {Namespace}.{classname}, {AssemblyName}");

                lock (_loadedTypesLock)
                    _loadedTypes[classname] = type;
            }

            return (T)(Activator.CreateInstance(type, args) ?? throw new ArgumentException($"Failed to load {Namespace}.{classname}, {AssemblyName}"));
        }
        catch (Exception ex)
        {
            throw new NotImplementedException($"Failed to load Windows Component {classname}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates a new disposable SeBackupPrivilegeScope that reatins the scope until disposed
    /// </summary>
    /// <returns>A disposable instance</returns>
    public static IDisposable NewSeBackupPrivilegeScope()
        => LoadWithReflection<IDisposable>("SeBackupPrivilegeScope");

    /// <summary>
    /// Creates a new PowerModeProvider that can notify of suspend/resume events
    /// </summary>
    /// <returns>A new PowerModeProvider</returns>
    public static IPowerModeProvider NewPowerModeProvider()
        => LoadWithReflection<IPowerModeProvider>("PowerManagementModule");

    /// <summary>
    /// Creates a new BackupDataStream for reading data with BackupRead
    /// </summary>
    /// <param name="path">The path to wrap</param>
    /// <returns>A new BackupDataStream</returns>
    public static Stream NewBackupDataStream(string path)
        => LoadWithReflection<Stream>("BackupDataStream", path);

    /// <summary>
    /// Loads the chosen snapshot provider
    /// </summary>
    /// <param name="provider">The provider to load</param>
    /// <returns>The snapshot provider</returns>
    public static ISnapshotProvider GetSnapshotProvider(WindowsSnapshotProvider provider)
        => provider switch
        {
            // To simplify things, we have AlphaVSS in the shim loader,
            // even though it is not loaded by reflection
            WindowsSnapshotProvider.AlphaVSS => new AlphaVssBackup(),
            WindowsSnapshotProvider.Vanara => LoadWithReflection<ISnapshotProvider>("VanaraVssBackup"),
            WindowsSnapshotProvider.Wmic => LoadWithReflection<ISnapshotProvider>("WmicVssBackup"),
            _ => throw new ArgumentException($"Invalid provider: {provider}")
        };
}