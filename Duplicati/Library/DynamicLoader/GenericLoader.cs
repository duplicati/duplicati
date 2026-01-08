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
using System.Linq;
using System.Reflection;
using Duplicati.Library.Interface;

namespace Duplicati.Library.DynamicLoader
{
    /// <summary>
    /// An interface class that provides easy loading of pluggable setting controls
    /// </summary>
    public static class GenericLoader
    {
        private const string BUILTIN_GENERIC_MODULES_ASSEMBLY_NAME = "Duplicati.Library.Modules.Builtin";

        private static IEnumerable<IGenericModule> GetBuiltInGenericModules()
        {
            // We avoid a compile-time dependency on Duplicati.Library.Modules.Builtin here to keep
            // the dependency direction clean (DynamicLoader should not require Builtin modules).
            //
            // Instead, we attempt to load the built-in modules assembly by name and reflect its
            // exported types to find IGenericModule implementations.
            Assembly? builtinAsm = null;
            try
            {
                builtinAsm = Assembly.Load(BUILTIN_GENERIC_MODULES_ASSEMBLY_NAME);
            }
            catch
            {
                // Ignore: built-in modules might not be present in all build configurations.
            }

            if (builtinAsm == null)
                yield break;

            Type[] types;
            try
            {
                types = builtinAsm.GetExportedTypes();
            }
            catch
            {
                yield break;
            }

            foreach (var t in types)
            {
                if (!typeof(IGenericModule).IsAssignableFrom(t))
                    continue;
                if (t.IsAbstract || !t.IsClass)
                    continue;
                if (t.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                IGenericModule? module = null;
                try
                {
                    module = Activator.CreateInstance(t) as IGenericModule;
                }
                catch
                {
                    // Ignore misbehaving modules during discovery.
                }

                if (module != null)
                    yield return module;
            }
        }

        /// <summary>
        /// Implementation overrides specific to generic module use
        /// </summary>
        private class GenericLoaderSub : DynamicLoader<IGenericModule>
        {
            /// <summary>
            /// Returns the filename extension, which is also the key
            /// </summary>
            /// <param name="item">The item to load the key for</param>
            /// <returns>The file extension used by the module</returns>
            protected override string GetInterfaceKey(IGenericModule item)
                => item.Key;

            /// <summary>
            /// Returns the subfolders searched for generic modules
            /// </summary>
            protected override string[] Subfolders
                => ["modules"];

            /// <summary>
            /// The built-in modules
            /// </summary>
            protected override IEnumerable<IGenericModule> BuiltInModules
                => GetBuiltInGenericModules();

            /// <summary>
            /// Creates a new instance of the module based on the key
            /// </summary>
            /// <param name="key">The key to create the instance for</param>
            /// <returns>The instanciated module or null if the key is not supported</returns>
            public IGenericModule? GetModule(string key)
            {
                LoadInterfaces();
                var entry = Interfaces.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    return null;

                return (IGenericModule?)Activator.CreateInstance(entry.GetType());
            }
        }

        #region Public static API

        /// <summary>
        /// The loader instance used to query the modules
        /// </summary>
        private static readonly Lazy<GenericLoaderSub> _loader = new(() => new GenericLoaderSub());
        /// <summary>
        /// Gets a list of loaded settings controls, the instances can be used to extract interface information, not used to interact with the module.
        /// </summary>
        public static IGenericModule[] Modules => _loader.Value.Interfaces;

        /// <summary>
        /// Gets a list of keys supported
        /// </summary>
        public static string[] Keys => _loader.Value.Keys;

        /// <summary>
        /// Instanciates a specific module
        /// </summary>
        /// <param name="key">The key to create the instance for</param>
        /// <returns>The instanciated backend or null if the key is not supported</returns>
        public static IGenericModule? GetModule(string key) => _loader.Value.GetModule(key);

        #endregion

    }
}
