// Immediate custom action scheduled in the InstallUISequence after
// ExecuteAction returns. Confirms that the Duplicati Windows Service
// successfully applied the freshly generated password before the ExitDialog
// shows it to the user.
//
// We poll HKLM\SOFTWARE\DuplicatiTeam\Duplicati\InstallState for the
// BootstrapApplied sentinel, which is written by
// ServiceControl.ScheduleInitPasswordCleanup once the server has started,
// consumed the password, and committed it to the database. If the sentinel
// appears within POLL_TIMEOUT_MS, leave DUPLICATI_SERVICE_PASSWORD intact
// so the ExitDialog shows it. Otherwise the bootstrap is presumed to have
// failed (the service didn't start, the server crashed before applying,
// etc.) and we clear the property so the password is not used anywhere.
//
// Runs in the client UI process. Both the registry probe (reg.exe) and
// the inter-poll sleep (timeout.exe) are launched via WScript.Shell.Run
// with vbHide=0; we use Run (not Exec) for the reg.exe call because Exec
// always shows the child's console window, while Run honors the hide flag.
// Presence of the value alone is checked by reg.exe's exit code (0 when
// the value exists, non-zero otherwise), so no stdout parsing is needed.

var POLL_TIMEOUT_MS = 35 * 1000;
var POLL_INTERVAL_SECONDS = 1;
var REGISTRY_PATH = "HKLM\\SOFTWARE\\DuplicatiTeam\\Duplicati\\InstallState";

function LogMessage(message) {
    var record = Session.Installer.CreateRecord(1);
    record.StringData(1) = message;
    var INSTALLMESSAGE_INFO = 0x04000000;
    Session.Message(INSTALLMESSAGE_INFO, record);
}

// Returns true if HKLM\...\InstallState\BootstrapApplied exists.
// ServiceControl is the only writer and writes "1", if the password was
// applied, zero if a password was already present.
//
// IMPORTANT: the MSI JScript custom action host on a per-machine install
// runs in the unprivileged user's *32-bit* msiexec context (even on x64
// Windows), so it is subject to WOW64 registry redirection:
// HKLM\SOFTWARE\... is silently rewritten to HKLM\SOFTWARE\WOW6432Node\...
// where our key does NOT exist. We observed this empirically:
//   * reg query from a regular cmd as the same user finds the value.
//   * WshShell.RegRead from this CA returns "not present" forever.
// We therefore bypass WshShell.RegRead and use WMI's StdRegProv class
// from the explicit 64-bit registry view via the __ProviderArchitecture
// context. StdRegProv.GetStringValue returns 0 on success and the value
// in sValue; any other return code or empty value means "not present".
function IsBootstrapApplied() {
    try {
        var reg = GetStdRegProv64();
        if (reg === null) {
            return null;
        }

        // GetStringValue signature: (hDefKey, sSubKeyName, sValueName, [out] sValue)
        // hDefKey 0x80000002 = HKEY_LOCAL_MACHINE
        var inParams = reg.provider.Methods_("GetStringValue").InParameters.SpawnInstance_();
        inParams.hDefKey = 0x80000002;
        inParams.sSubKeyName = "SOFTWARE\\DuplicatiTeam\\Duplicati\\InstallState";
        inParams.sValueName = "BootstrapApplied";

        var outParams = reg.provider.ExecMethod_("GetStringValue", inParams, 0, reg.ctx);
        if (outParams.ReturnValue !== 0) {
            return null;
        }
        if (outParams.sValue === "0") {
            return false;
        }
        if (outParams.sValue === "1") {
            return true;
        }
        return null;
    } catch (e) {
        return null;
    }
}

// Returns an object { provider, ctx } pinned to the 64-bit registry view,
// or null on failure. Pulled out of IsBootstrapApplied so DeleteBootstrapApplied
// can reuse the same WOW64-bypass path.
function GetStdRegProv64() {
    try {
        var locator = new ActiveXObject("WbemScripting.SWbemLocator");
        var ctx = new ActiveXObject("WbemScripting.SWbemNamedValueSet");
        ctx.Add("__ProviderArchitecture", 64);
        ctx.Add("__RequiredArchitecture", true);

        var services = locator.ConnectServer(".", "root\\default", "", "", null, null, 0, ctx);
        return { provider: services.Get("StdRegProv"), ctx: ctx };
    } catch (e) {
        return null;
    }
}

// Deletes the BootstrapApplied sentinel value after we have consumed it.
function DeleteBootstrapApplied() {
    try {
        var reg = GetStdRegProv64();
        if (reg === null) {
            return;
        }

        // DeleteValue signature: (hDefKey, sSubKeyName, sValueName)
        // hDefKey 0x80000002 = HKEY_LOCAL_MACHINE
        var inParams = reg.provider.Methods_("DeleteValue").InParameters.SpawnInstance_();
        inParams.hDefKey = 0x80000002;
        inParams.sSubKeyName = "SOFTWARE\\DuplicatiTeam\\Duplicati\\InstallState";
        inParams.sValueName = "BootstrapApplied";

        reg.provider.ExecMethod_("DeleteValue", inParams, 0, reg.ctx);
    } catch (e) {
        /* ignore */
    }
}

// Sleeps for the given number of seconds without showing a console window.
function SleepSeconds(seconds) {
    try {
        var shell = new ActiveXObject("WScript.Shell");
        // /nobreak suppresses Ctrl+C interruption; redirecting stdout to nul
        // hides the "Waiting, X seconds left ..." countdown. vbHide=0 keeps
        // the cmd/timeout windows hidden.
        shell.Run("cmd /c timeout /t " + seconds + " /nobreak > nul", 0, true);
    } catch (sleepEx) {
        // If we can't sleep, busy-loop a bit so we don't peg CPU.
        var t0 = (new Date()).getTime();
        while ((new Date()).getTime() - t0 < seconds * 1000) { /* spin */ }
    }
}

function ClearPassword() {
    Session.Property("DUPLICATI_SERVICE_PASSWORD") = "";
}

function CustomAction() {
    try {
        // Skip if the service install path was never taken.
        var pwd = Session.Property("DUPLICATI_SERVICE_PASSWORD");
        if (!pwd || pwd.length === 0) {
            return 0;
        }

        LogMessage("CheckBootstrapResult: starting poll for BootstrapApplied (timeout="
            + (POLL_TIMEOUT_MS / 1000) + "s, interval=" + POLL_INTERVAL_SECONDS + "s, key="
            + REGISTRY_PATH + ").");

        var pollCount = 0;
        var deadline = (new Date()).getTime() + POLL_TIMEOUT_MS;
        while ((new Date()).getTime() < deadline) {
            pollCount++;
            var status = IsBootstrapApplied();
            if (status === true) {
                LogMessage("CheckBootstrapResult: BootstrapApplied sentinel detected on poll #"
                    + pollCount + "; password is live in the server.");
                DeleteBootstrapApplied();
                return 0;
            } else if (status === false) {
                LogMessage("CheckBootstrapResult: BootstrapApplied sentinel explicitly rejected on poll #"
                    + pollCount + "; clearing DUPLICATI_SERVICE_PASSWORD.");
                ClearPassword();
                DeleteBootstrapApplied();
                return 0;
            }
            // Log progress every 10 polls so the MSI log shows the loop is alive.
            if (pollCount === 1 || pollCount % 10 === 0) {
                LogMessage("CheckBootstrapResult: poll #" + pollCount
                    + " - BootstrapApplied not yet present, sleeping "
                    + POLL_INTERVAL_SECONDS + "s.");
            }
            SleepSeconds(POLL_INTERVAL_SECONDS);
        }

        LogMessage("CheckBootstrapResult: BootstrapApplied sentinel did not appear within "
            + (POLL_TIMEOUT_MS / 1000) + "s (" + pollCount + " polls); clearing DUPLICATI_SERVICE_PASSWORD.");
        ClearPassword();
        return 0;
    } catch (e) {
        LogMessage("CheckBootstrapResult: error: " + e.message);
        // On unexpected error, clear the password to avoid showing a value
        // we can't verify.
        try { ClearPassword(); } catch (clearEx) { /* ignore */ }
        return 0;
    }
}
