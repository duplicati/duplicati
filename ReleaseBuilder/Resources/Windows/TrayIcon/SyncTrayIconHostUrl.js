// Immediate custom action scheduled in the InstallUISequence after ExecuteAction,
// when the service was installed before and stays installed (an upgrade, a
// repair or a Change). Calls
//   Duplicati.CommandLine.SecretTool.exe set wincred:// duplicati-trayicon-hosturl <URL> --overwrite
// with the URL that TRAYICON_HOSTURL was set to, so the TrayIcon connects to
// the service with the scheme the service now uses.
//
// StoreTrayIconValues only stores the host URL when the service is installed
// for the first time. Turning TLS on in an upgrade or in Change switched the
// service to HTTPS while the TrayIcon kept using HTTP, and turning TLS off left
// the TrayIcon on HTTPS (#7804). The wxs sets TRAYICON_HOSTURL only when the
// TLS feature is being installed or removed, or in an upgrade, so a host URL
// the user entered is kept on a plain repair.
//
// Being immediate and in the InstallUISequence (which runs as the installing
// user, not LocalSystem), we write to that user's Credential Vault, as
// StoreTrayIconValues does.

function LogMessage(message) {
    var record = Session.Installer.CreateRecord(1);
    record.StringData(1) = message;
    var INSTALLMESSAGE_INFO = 0x04000000;
    Session.Message(INSTALLMESSAGE_INFO, record);
}

function CustomAction() {
    try {
        var url = Session.Property("TRAYICON_HOSTURL");
        if (!url || url.length === 0) {
            return 0;
        }

        var installDir = Session.Property("INSTALLLOCATION");
        if (!installDir || installDir.length === 0) {
            LogMessage("SyncTrayIconHostUrl: INSTALLLOCATION is empty, skipping.");
            return 0;
        }

        if (installDir.charAt(installDir.length - 1) !== "\\") {
            installDir += "\\";
        }
        var exePath = installDir + "Duplicati.CommandLine.SecretTool.exe";

        var fso = new ActiveXObject("Scripting.FileSystemObject");
        if (!fso.FileExists(exePath)) {
            LogMessage("SyncTrayIconHostUrl: SecretTool.exe not found at " + exePath + ", skipping.");
            return 0;
        }

        var shell = new ActiveXObject("WScript.Shell");
        // The URL is one of two fixed values set by the wxs, so no escaping is required.
        var cmd = "\"" + exePath + "\""
                + " set wincred:// duplicati-trayicon-hosturl \"" + url + "\""
                + " --overwrite";

        var rc = shell.Run(cmd, 0, true);
        if (rc !== 0) {
            // Don't fail the install; the user can still enter the URL in the TrayIcon
            LogMessage("SyncTrayIconHostUrl: SecretTool.exe exited with code " + rc + ".");
            return 0;
        }

        LogMessage("SyncTrayIconHostUrl: stored duplicati-trayicon-hosturl " + url + " in user credential store.");
        return 0;
    } catch (e) {
        LogMessage("SyncTrayIconHostUrl: error: " + e.message);
        return 0;
    }
}
