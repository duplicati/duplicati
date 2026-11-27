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
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Duplicati.WindowsService
{
    /// <summary>
    /// Registers the signature catalog with Windows.
    ///
    /// Duplicati.cat lists the hashes of the third-party libraries that their
    /// publishers do not sign, and is signed by Duplicati Inc. Once it is added
    /// to the system catalog database, Windows code integrity (Smart App Control
    /// and App Control for Business) treats the listed files as signed.
    ///
    /// This runs before the catalog is registered, so it must only use our own
    /// signed assemblies and the .NET runtime.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class SignatureCatalog
    {
        /// <summary>
        /// The name of the catalog file, next to the executable
        /// </summary>
        public const string CatalogFileName = "Duplicati.cat";

        /// <summary>
        /// DRIVER_ACTION_VERIFY, the catalog database that code integrity uses
        /// </summary>
        private static readonly Guid DriverActionVerify = new Guid("F750E6C3-38EE-11D1-85E5-00C04FC295EE");

        /// <summary>
        /// The hash algorithm of the catalog
        /// </summary>
        private const string HashAlgorithm = "SHA256";

        private const int ERROR_FILE_NOT_FOUND = 2;
        private const int ERROR_PATH_NOT_FOUND = 3;
        private const int ERROR_NOT_FOUND = 1168;

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptCATAdminAcquireContext2(out IntPtr phCatAdmin, in Guid pgSubsystem, string pwszHashAlgorithm, IntPtr pStrongHashPolicy, uint dwFlags);

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CryptCATAdminAddCatalog(IntPtr hCatAdmin, string pwszCatalogFile, string pwszSelectBaseName, uint dwFlags);

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint dwFlags);

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptCATAdminRemoveCatalog(IntPtr hCatAdmin, string pwszCatalogFile, uint dwFlags);

        /// <summary>
        /// Gets the name the catalog is registered under. The name is derived from
        /// the contents, so each build has its own registration, and an installation
        /// of one build does not remove the registration of another.
        /// </summary>
        /// <param name="catalogFile">The catalog file</param>
        /// <returns>The registered name</returns>
        private static string GetRegisteredName(string catalogFile)
        {
            using var stream = File.OpenRead(catalogFile);
            return $"Duplicati-{Convert.ToHexString(SHA256.HashData(stream))[..16]}.cat";
        }

        /// <summary>
        /// Runs an operation with a handle to the catalog database
        /// </summary>
        /// <param name="operation">The operation to run</param>
        private static void WithCatalogAdmin(Action<IntPtr> operation)
        {
            if (!CryptCATAdminAcquireContext2(out var admin, in DriverActionVerify, HashAlgorithm, IntPtr.Zero, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                operation(admin);
            }
            finally
            {
                CryptCATAdminReleaseContext(admin, 0);
            }
        }

        /// <summary>
        /// Adds the catalog to the system catalog database
        /// </summary>
        /// <param name="folder">The folder with the catalog file</param>
        /// <returns>The process exit code</returns>
        public static int Register(string folder)
        {
            var catalogFile = Path.Combine(folder, CatalogFileName);
            if (!File.Exists(catalogFile))
            {
                // Builds that are not signed have no catalog
                Console.WriteLine($"No signature catalog found at {catalogFile}, nothing to register.");
                return 0;
            }

            try
            {
                var name = GetRegisteredName(catalogFile);
                WithCatalogAdmin(admin =>
                {
                    var info = CryptCATAdminAddCatalog(admin, catalogFile, name, 0);
                    if (info == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    CryptCATAdminReleaseCatalogContext(admin, info, 0);
                });

                Console.WriteLine($"Registered the signature catalog {catalogFile} as {name}.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to register the signature catalog {catalogFile}: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Removes the catalog from the system catalog database
        /// </summary>
        /// <param name="folder">The folder with the catalog file</param>
        /// <returns>The process exit code</returns>
        public static int Unregister(string folder)
        {
            var catalogFile = Path.Combine(folder, CatalogFileName);
            if (!File.Exists(catalogFile))
            {
                Console.WriteLine($"No signature catalog found at {catalogFile}, nothing to unregister.");
                return 0;
            }

            try
            {
                var name = GetRegisteredName(catalogFile);
                var removed = true;
                WithCatalogAdmin(admin =>
                {
                    if (!CryptCATAdminRemoveCatalog(admin, name, 0))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND && error != ERROR_NOT_FOUND)
                            throw new Win32Exception(error);
                        removed = false;
                    }
                });

                Console.WriteLine(removed
                    ? $"Unregistered the signature catalog {name}."
                    : $"The signature catalog {name} was not registered.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to unregister the signature catalog {catalogFile}: {ex.Message}");
                return 1;
            }
        }
    }
}
