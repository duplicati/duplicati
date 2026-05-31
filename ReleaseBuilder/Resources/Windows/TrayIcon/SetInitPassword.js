// Deferred MSI custom-action wrapper that hands the InitPassword to
// Duplicati.WindowsService.exe via an environment variable rather than
// the command line, so the password never appears in process listings
// or in the MSI verbose log (MsiHiddenProperties redacts the property
// value, but command-line arguments would be visible to anything that
// can enumerate processes for the brief lifetime of the child).
//
// CustomActionData format:
//     <exe-path>|<password>
// We split on the FIRST '|' only because passwords may legitimately
// contain '|'. The exe path itself never contains '|' (it is a
// formatted-string substitution of [#Duplicati.WindowsService.exe]).
//
// The launched binary runs `set-init-password`, which reads
// DUPLICATI__INIT_PASSWORD, atomically recreates the locked-down
// Service registry key, writes InitPassword into it, and exits 0 on
// success / 1 on failure.
function RunSetInitPassword() {
    var shell = new ActiveXObject("WScript.Shell");

    var data = Session.Property("CustomActionData");
    if (!data || data.length === 0) {
        // No data passed - fail loudly. The MSI engine will surface
        // this as a CA failure because Return="check" is set on the
        // matching <CustomAction>.
        throw new Error("SetInitPassword: CustomActionData is empty.");
    }

    var sep = data.indexOf("|");
    if (sep < 0) {
        throw new Error("SetInitPassword: CustomActionData is missing the '|' separator.");
    }

    var exe = data.substring(0, sep);
    var password = data.substring(sep + 1);

    if (exe.length === 0) {
        throw new Error("SetInitPassword: empty exe path in CustomActionData.");
    }
    if (password.length === 0) {
        throw new Error("SetInitPassword: empty password in CustomActionData.");
    }

    // Set the env var on the current script's process. WScript.Shell.Run
    // launches the child as a child process of this script process, so
    // the env var is inherited. The variable is NOT promoted to user-
    // or system-wide because we use the "PROCESS" environment scope.
    shell.Environment("PROCESS").Item("DUPLICATI__INIT_PASSWORD") = password;
    try {
        // First arg quotes the exe path itself, then appends the
        // sub-command. Quoting the exe is necessary because the install
        // dir may contain spaces. Window-style 0 hides the window;
        // 'true' makes Run wait for the child to exit and return its
        // exit code.
        var cmd = "\"" + exe + "\" set-init-password";
        var rc = shell.Run(cmd, 0, true);
        if (rc !== 0) {
            throw new Error("SetInitPassword: child process exited with code " + rc);
        }
    } finally {
        // Clear the env var from our process before we exit. The child
        // already cleared its own copy as soon as it read the value;
        // this clears our copy so it does not survive in any process
        // memory snapshot taken between now and script exit.
        shell.Environment("PROCESS").Item("DUPLICATI__INIT_PASSWORD") = "";
    }
}
