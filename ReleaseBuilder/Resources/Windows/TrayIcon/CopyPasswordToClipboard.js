// Copies the value of DUPLICATI_SERVICE_PASSWORD to the Windows clipboard.
// Wired to the "Copy" PushButton on the ExitDialog.
//
// To avoid the password appearing on the cmd.exe command line (where it
// would be visible to any process that enumerates running command lines via
// WMI's Win32_Process.CommandLine for the brief lifetime of the cmd /
// clip processes), the password is staged in a process-scoped environment
// variable on the msiexec process. The cmd line then references the
// variable name. Process monitors see only "%MSI_TEMP_SECRET%". The
// variable is removed immediately after clip.exe returns; if msiexec exits
// abnormally before the removal, the variable is volatile (process scope)
// and dies with the process.

function LogMessage(message) {
    var record = Session.Installer.CreateRecord(1);
    record.StringData(1) = message;
    var INSTALLMESSAGE_INFO = 0x04000000;
    Session.Message(INSTALLMESSAGE_INFO, record);
}

function CustomAction() {
    var env = null;
    try {
        var pwd = Session.Property("DUPLICATI_SERVICE_PASSWORD");
        if (!pwd || pwd.length === 0) {
            LogMessage("CopyPasswordToClipboard: nothing to copy.");
            return 0;
        }

        // Copy to clipboard without showing the password in the command line.
        var shell = new ActiveXObject("WScript.Shell");
        env = shell.Environment("Process");
        env.Item("MSI_TEMP_SECRET") = pwd;

        var cmd = "cmd /c echo|set /p=%MSI_TEMP_SECRET%| clip";
        shell.Run(cmd, 0, true);
        
        env.Remove("MSI_TEMP_SECRET");
        env = null;

        LogMessage("CopyPasswordToClipboard: copied password to clipboard.");
        return 0;
    } catch (e) {
        LogMessage("CopyPasswordToClipboard: error: " + e.message);
        // Best-effort cleanup if we set the env var before throwing.
        try {
            if (env != null) {
                env.Remove("MSI_TEMP_SECRET");
            }
        } catch (cleanupEx) { /* ignore */ }
        return 0;
    }
}
