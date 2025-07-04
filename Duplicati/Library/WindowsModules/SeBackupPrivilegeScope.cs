
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
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vanara.PInvoke;
using Vanara.Security.AccessControl;
using static Vanara.PInvoke.AdvApi32;
using static Vanara.PInvoke.Kernel32;

namespace Duplicati.Library.WindowsModules;

/// <summary>
/// A scope that enables the SeBackupPrivilege for the current process.
/// This privilege is required to read files that are not accessible
/// due to access control restrictions, such as files owned by other users.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SeBackupPrivilegeScope : IDisposable
{
    /// <summary>
    /// The token handle for the current process with the SeBackupPrivilege enabled.
    /// </summary>
    private readonly SafeHTOKEN _token;
    /// <summary>
    /// The original state of the SeBackupPrivilege before enabling it.
    /// </summary>
    private readonly TOKEN_PRIVILEGES _originalState;
    /// <summary>
    /// Indicates whether the scope has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeBackupPrivilegeScope"/> class,
    /// and enables the SeBackupPrivilege for the current process.
    /// </summary>
    public SeBackupPrivilegeScope()
    {
        const TokenAccess Access = TokenAccess.TOKEN_ADJUST_PRIVILEGES | TokenAccess.TOKEN_QUERY;

        if (!OpenProcessToken(GetCurrentProcess(), Access, out _token))
            throw Win32Error.GetLastError().GetException()
               ?? new Win32Exception(Marshal.GetLastWin32Error());

        _originalState = _token.AdjustPrivilege(SystemPrivilege.Backup, PrivilegeAttributes.SE_PRIVILEGE_ENABLED);

        // Even if AdjustPrivilege "succeeds", we must verify it worked:
        if (Win32Error.GetLastError() == Win32Error.ERROR_NOT_ALL_ASSIGNED)
        {
            _token.Dispose();
            throw new InvalidOperationException("SeBackupPrivilege is not available or cannot be enabled.");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _token.AdjustPrivileges(_originalState);
        }
        finally
        {
            _token.Dispose();
            _disposed = true;
        }
    }
}