function RunAppHidden() {
    var shell = new ActiveXObject("WScript.Shell");
    // Reads the command string passed from the MSI
    var command = Session.Property("CustomActionData");
    // The '0' argument hides the window entirely, 'true' makes the installer wait for it to finish
    shell.Run(command, 0, true);
}
