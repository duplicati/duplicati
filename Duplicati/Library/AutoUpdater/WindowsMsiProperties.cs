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
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// Derives the properties a silent MSI upgrade needs to keep the current install configuration.
/// A silent install cannot read them itself: AppSearch runs after the early RemoveExistingProducts
/// has removed the previous version, and with it the recorded values.
/// </summary>
public static class WindowsMsiProperties
{
    /// <summary>
    /// The registry key the installer records its state in, below HKLM\SOFTWARE
    /// </summary>
    private const string InstallStateKey = @"SOFTWARE\DuplicatiTeam\Duplicati\InstallState";

    /// <summary>
    /// The registry key below HKCU with the marker for the startup entry of the current user only
    /// </summary>
    private const string UserStartupKey = @"Software\DuplicatiTeam\Duplicati\UserStartupShortcut";

    /// <summary>
    /// Derives the properties for the running installation
    /// </summary>
    /// <param name="host">The host environment</param>
    /// <param name="installDir">The install folder</param>
    /// <returns>The properties to pass to msiexec</returns>
    public static Dictionary<string, string> Derive(HostEnvironment host, string installDir)
    {
        var props = new Dictionary<string, string>();
        if (!OperatingSystem.IsWindows())
            return props;

        // A trailing backslash before the closing quote would be swallowed by the command line parser
        props["INSTALLLOCATION"] = installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var serviceInstalled = host.Supervisor == SupervisorKind.WindowsService || ReadMarker("ServiceInstalled");
        var tlsServiceCerts = ReadMarker("TlsCertsServiceInstalled");
        var tlsUserCerts = ReadMarker("TlsCertsUserInstalled");

        if (serviceInstalled)
        {
            var features = new List<string> { "DuplicatiCore", "DuplicatiProgramMenuShortCutFeature", "DuplicatiServiceFeature" };
            if (tlsServiceCerts || tlsUserCerts)
                features.Add("DuplicatiTlsCertsFeature");

            props["ADDLOCAL"] = string.Join(",", features);
            props["DUPLICATI_SERVICE_SELECTED"] = "1";
            props["INSTALLASSERVICE"] = "true";
            props["FORSERVICE"] = "true";
        }
        else
        {
            // The service install has no startup entry (FORSERVICE), and as a service this process
            // does not run as the user who owns the marker, so this is only read here
            var userStartup = ReadUserStartupMarker();
            if (userStartup)
                props["STARTUP_FOR_CURRENT_USER"] = "1";

            if (tlsUserCerts)
            {
                var startupFeature = userStartup ? "DuplicatiUserStartupShortCutFeature" : "DuplicatiStartupShortCutFeature";
                props["ADDLOCAL"] = $"DuplicatiCore,DuplicatiProgramMenuShortCutFeature,{startupFeature},DuplicatiTlsCertsFeature";
            }
        }

        if (tlsServiceCerts || tlsUserCerts)
            props["INSTALL_TLS_CERTS"] = "true";

        return props;
    }

    /// <summary>
    /// Reads the marker the installer writes when Duplicati is started at logon for the installing user only.
    /// It is in HKCU, so it is only found when this process runs as that user.
    /// </summary>
    /// <returns><c>true</c> if the marker is set</returns>
    [SupportedOSPlatform("windows")]
    private static bool ReadUserStartupMarker()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UserStartupKey, false);
            return key?.GetValue("Enabled") as string == "1";
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool ReadMarker(string name)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(InstallStateKey, false);
            var value = key?.GetValue(name) as string;
            return !string.IsNullOrWhiteSpace(value) && value != "0";
        }
        catch
        {
            return false;
        }
    }
}
