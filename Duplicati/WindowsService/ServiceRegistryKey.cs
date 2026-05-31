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
using System.Runtime.Versioning;
using System.Security.AccessControl;
using Microsoft.Win32;

namespace Duplicati.WindowsService
{
    /// <summary>
    /// Helpers for managing the <c>HKLM\SOFTWARE\DuplicatiTeam\Duplicati\Service</c>
    /// registry key used to ferry secrets between elevated installer/CLI and the
    /// elevated Duplicati Windows Service.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class ServiceRegistryKey
    {
        /// <summary>
        /// Sub-key path under HKLM for the protected Service registry key.
        /// </summary>
        internal const string SUBKEY_PATH = ServiceControl.INIT_REGISTRY_KEY;

        /// <summary>
        /// Expected SDDL form of the DACL on the Service registry key.
        ///   D:P                            - protected DACL, no inheritance
        ///   (A;OICI;KA;;;SY)               - SYSTEM: full key access, inherit
        ///   (A;OICI;KA;;;BA)               - Built-in Administrators: full key access, inherit
        /// </summary>
        internal const string EXPECTED_SDDL = "D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)";

        /// <summary>
        /// Builds a <see cref="RegistrySecurity"/> instance matching
        /// <see cref="EXPECTED_SDDL"/> for atomic application at key
        /// creation time.
        /// </summary>
        private static RegistrySecurity BuildExpectedSecurity()
        {
            var security = new RegistrySecurity();
            security.SetSecurityDescriptorSddlForm(EXPECTED_SDDL);
            return security;
        }

        /// <summary>
        /// Compares the actual DACL (in SDDL form) against the expected
        /// locked-down DACL semantically.
        /// </summary>
        /// <param name="actualSddl">SDDL form of the DACL on the actual key</param>
        /// <param name="detail">On mismatch, a short human-readable explanation of which requirement failed; <c>null</c> on success.
        /// </param>
        private static bool DaclMatchesExpected(string actualSddl, out string? detail)
        {
            detail = null;
            if (string.IsNullOrEmpty(actualSddl))
            {
                detail = "empty DACL";
                return false;
            }

            // Parse the actual DACL through CommonSecurityDescriptor so
            // we get a canonical, normalized view we can walk ACE-by-ACE.
            // The expected side is parsed the same way from the static
            // EXPECTED_SDDL constant so both sides flow through the
            // same canonicalization path.
            CommonSecurityDescriptor actual;
            CommonSecurityDescriptor expected;
            try
            {
                // isContainer:true matches a registry key (which is a
                // container, holding subkeys and values).
                actual = new CommonSecurityDescriptor(isContainer: true, isDS: false, sddlForm: actualSddl);
                expected = new CommonSecurityDescriptor(isContainer: true, isDS: false, sddlForm: EXPECTED_SDDL);
            }
            catch (Exception ex)
            {
                detail = "could not parse DACL: " + ex.Message;
                return false;
            }

            // The DACL must be protected from inheritance.
            if ((actual.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0)
            {
                detail = "DACL is not protected from inheritance (SE_DACL_PROTECTED flag missing)";
                return false;
            }

            // Compare ACE sets. We require the actual ACL to be a
            // permutation of the expected ACL: same number of ACEs,
            // each expected ACE matched exactly once by an actual ACE
            // with identical type / flags / access mask / SID.
            var expectedAces = expected.DiscretionaryAcl;
            var actualAces = actual.DiscretionaryAcl;
            if (expectedAces == null)
            {
                detail = "expected DACL is empty";
                return false;
            }

            if (actualAces == null)
            {
                detail = "actual DACL is empty";
                return false;
            }

            if (actualAces.Count != expectedAces.Count)
            {
                detail = $"DACL has {actualAces.Count} ACE(s), expected {expectedAces.Count}";
                return false;
            }

            var actualMatched = new bool[actualAces.Count];
            foreach (GenericAce expectedAce in expectedAces)
            {
                if (expectedAce is not CommonAce expectedCommon)
                {
                    detail = "unsupported expected ACE type: " + expectedAce.GetType().Name;
                    return false;
                }

                var foundMatch = false;
                for (var i = 0; i < actualAces.Count; i++)
                {
                    if (actualMatched[i])
                        continue;
                    if (actualAces[i] is not CommonAce actualCommon)
                        continue;
                    if (actualCommon.AceType != expectedCommon.AceType)
                        continue;
                    if (actualCommon.AceFlags != expectedCommon.AceFlags)
                        continue;
                    if (actualCommon.AccessMask != expectedCommon.AccessMask)
                        continue;
                    if (!actualCommon.SecurityIdentifier.Equals(expectedCommon.SecurityIdentifier))
                        continue;
                    actualMatched[i] = true;
                    foundMatch = true;
                    break;
                }

                if (!foundMatch)
                {
                    detail = $"missing expected ACE for SID {expectedCommon.SecurityIdentifier}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Deletes the Service subtree and recreates the key with the
        /// locked-down DACL applied at creation. The returned handle is
        /// writable; callers should write the single value they need,
        /// then dispose immediately.
        /// </summary>
        /// <returns>An open writable handle to the freshly created key.</returns>
        internal static RegistryKey RecreateLockedDown()
        {
            var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            // Wipe the leaf first so no stale value can survive.
            view.DeleteSubKeyTree(SUBKEY_PATH, throwOnMissingSubKey: false);

            // Split SUBKEY_PATH into <parent path>\<leaf name>. SUBKEY_PATH
            // is a constant ending in "\Service" so this always produces a
            // non-empty parent and a non-empty leaf.
            var lastSep = SUBKEY_PATH.LastIndexOf('\\');
            if (lastSep <= 0 || lastSep >= SUBKEY_PATH.Length - 1)
                throw new System.IO.IOException(
                    $"Invalid SUBKEY_PATH '{SUBKEY_PATH}': cannot split parent/leaf");

            var parentPath = SUBKEY_PATH.Substring(0, lastSep);
            var leafName = SUBKEY_PATH.Substring(lastSep + 1);

            // Create the parent chain first with enherited security
            using var parent = view.CreateSubKey(parentPath, writable: true)
                ?? throw new System.IO.IOException(
                    $"Failed to create parent registry key {parentPath}");

            // Now create the leaf with the locked-down DACL applied atomically
            // at creation time. The DACL is set ONLY on the leaf key.
            var security = BuildExpectedSecurity();
            var key = parent.CreateSubKey(
                leafName,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                security);

            if (key == null)
                throw new System.IO.IOException(
                    $"Failed to create registry key {SUBKEY_PATH}");
            return key;
        }

        /// <summary>
        /// Opens the Service registry key for reading/writing only if its
        /// DACL exactly matches <see cref="EXPECTED_SDDL"/>. Returns
        /// <c>null</c> when the key does not exist, or when the ACL has been
        /// tampered with, or when access could not be checked.
        /// <param name="writable">Whether to open the key with write access.</param>
        /// <param name="reason">On <c>null</c> return, describes why
        /// (does-not-exist / acl-mismatch / failure). Useful for logging.</param>
        internal static RegistryKey? OpenIfTrusted(bool writable, out string? reason)
        {
            reason = null;
            RegistryKey? key = null;
            try
            {
                // RegistryKey instances are independent handles in
                // .NET so disposing the base key while keeping the
                // returned sub-key open is safe.
                using (var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    key = view.OpenSubKey(SUBKEY_PATH, writable: writable);
                }
                if (key == null)
                {
                    reason = "key-missing";
                    return null;
                }

                // Compare the DACL of th key with the expected one.
                var actualSecurity = key.GetAccessControl(AccessControlSections.Access);
                var actualSddl = actualSecurity.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
                if (!DaclMatchesExpected(actualSddl, out var detail))
                {
                    reason = $"acl-mismatch ({detail}); actual '{actualSddl}'";
                    key.Dispose();
                    return null;
                }

                return key;
            }
            catch (Exception ex)
            {
                reason = "access-failed: " + ex.Message;
                try { key?.Dispose(); } catch { }
                return null;
            }
        }
    }
}
