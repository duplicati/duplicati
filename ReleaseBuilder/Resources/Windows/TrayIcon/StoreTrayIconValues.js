// Immediate custom action scheduled in the InstallUISequence right after
// CheckBootstrapResult. Calls
//   Duplicati.CommandLine.SecretTool.exe set wincred:// duplicati-trayicon-password <RANDOM> --overwrite
// so that the TrayIcon (running in the user session) can pick up the
// password without prompting.
//
// CheckBootstrapResult clears DUPLICATI_SERVICE_PASSWORD if the BootstrapApplied
// registry sentinel does not appear within its poll timeout, and our wxs
// schedules this CA with a "DUPLICATI_SERVICE_PASSWORD" condition, so we
// only run when the bootstrap is verified to have succeeded. That guarantees
// we never plant a stale value the TrayIcon would keep retrying with.
//
// Being immediate and in the InstallUISequence (which runs as the installing
// user, not LocalSystem), we are already in the right security context to
// write to the user's Credential Vault, and we can read INSTALLLOCATION /
// DUPLICATI_SERVICE_PASSWORD straight from the session - no CustomActionData
// dance is needed.

function LogMessage(message) {
    var record = Session.Installer.CreateRecord(1);
    record.StringData(1) = message;
    var INSTALLMESSAGE_INFO = 0x04000000;
    Session.Message(INSTALLMESSAGE_INFO, record);
}

function CustomAction() {
    try {
        var password = Session.Property("DUPLICATI_SERVICE_PASSWORD");
        if (!password || password.length === 0) {
            // Either we are not in the service install path or
            // CheckBootstrapResult cleared the property because the
            // bootstrap failed. Either way, nothing to store.
            return 0;
        }

        var installDir = Session.Property("INSTALLLOCATION");
        if (!installDir || installDir.length === 0) {
            LogMessage("StoreTrayIconValues: INSTALLLOCATION is empty, skipping.");
            return 0;
        }

        if (installDir.charAt(installDir.length - 1) !== "\\") {
            installDir += "\\";
        }
        var exePath = installDir + "Duplicati.CommandLine.SecretTool.exe";

        var fso = new ActiveXObject("Scripting.FileSystemObject");
        if (!fso.FileExists(exePath)) {
            LogMessage("StoreTrayIconValues: SecretTool.exe not found at " + exePath + ", skipping.");
            return 0;
        }

        var shell = new ActiveXObject("WScript.Shell");
        // The password is alphanumeric so no shell-escaping is required.
        var cmd = "\"" + exePath + "\""
                + " set wincred:// duplicati-trayicon-password \"" + password + "\""
                + " --overwrite";

        var rc = shell.Run(cmd, 0, true);
        if (rc !== 0) {
            LogMessage("StoreTrayIconValues: SecretTool.exe exited with code " + rc + ".");
            // Don't fail the install if user-context secret storage fails;
            // the user can still log in by entering the password manually
            // (which is shown on the ExitDialog).
            return 0;
        }

        LogMessage("StoreTrayIconValues: stored duplicati-trayicon-password in user credential store.");

        var installTls = Session.Property("INSTALL_TLS_CERTS");
        if (installTls === "1") {
            var urlCmd = "\"" + exePath + "\""
                    + " set wincred:// duplicati-trayicon-hosturl \"https://127.0.0.1:8200\""
                    + " --overwrite";
            
            var urlRc = shell.Run(urlCmd, 0, true);
            if (urlRc !== 0) {
                LogMessage("StoreTrayIconValues: SecretTool.exe for hosturl exited with code " + urlRc + ".");
            } else {
                LogMessage("StoreTrayIconValues: stored duplicati-trayicon-hosturl in user credential store.");
            }
        }

        return 0;
    } catch (e) {
        LogMessage("StoreTrayIconValues: error: " + e.message);
        return 0;
    }
}
