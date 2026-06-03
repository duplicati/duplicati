// Writes the UpgradeDropServiceCerts sentinel that tells the nested uninstall
// of the OLD product (triggered by RemoveExistingProducts during a major
// upgrade) that it must remove the service TLS certificates while the old
// service binaries are still present. The matching RegistrySearch that seeds
// UPGRADE_DROP_SERVICE_CERTS reads the SAME HKCU path.
//
// Why HKCU (not HKLM): WriteUpgradeSentinel must run BEFORE RemoveExisting
// Products, which is scheduled early (before InstallInitialize), so it can only
// be an immediate CA. Immediate CAs run impersonated and non-elevated, so a
// write to HKLM\SOFTWARE silently fails. The installing user CAN write their
// own HKCU, and the nested uninstall's AppSearch resolves HKCU to the same
// user, so the sentinel round-trips reliably.
//
// WScript.Shell honors the current (impersonated) user token in-process; WMI's
// StdRegProv would instead resolve HKCU to the WMI service's SYSTEM hive. There
// is no WOW64 redirection for HKCU\Software (only HKCU\Software\Classes is).
function WriteUpgradeSentinel() {
  try {
    var shell = new ActiveXObject("WScript.Shell");
    shell.RegWrite(
      "HKCU\\SOFTWARE\\DuplicatiTeam\\Duplicati\\InstallState\\UpgradeDropServiceCerts",
      "1", "REG_SZ");
  } catch (e) {
    // Fail gracefully if error encountered
  }
}

function ClearUpgradeSentinel() {
  try {
    var shell = new ActiveXObject("WScript.Shell");
    shell.RegDelete(
      "HKCU\\SOFTWARE\\DuplicatiTeam\\Duplicati\\InstallState\\UpgradeDropServiceCerts");
  } catch (e) {
    // If the key is already gone, ignore the exception
  }
}

// Registry locations of the TLS-cert state markers. These mirror the
// RegistryValue elements in the marker components in Duplicati.wxs

var HKLM = 0x80000002;
var TLS_INSTALLSTATE_KEY = "SOFTWARE\\DuplicatiTeam\\Duplicati\\InstallState";
var TLS_SERVICE_KEY = "SOFTWARE\\DuplicatiTeam\\Duplicati\\Service";

// Returns { provider, ctx } pinned to the 64-bit registry view, or the default
// provider (ctx=null) on a genuine 32-bit OS.
function getRegProv() {
    var locator = new ActiveXObject("WbemScripting.SWbemLocator");

    try {
        var ctx = new ActiveXObject("WbemScripting.SWbemNamedValueSet");
        ctx.Add("__ProviderArchitecture", 64);
        ctx.Add("__RequiredArchitecture", true);
        var services64 = locator.ConnectServer(
            ".", "root\\default", "", "", null, null, 0, ctx);
        return { provider: services64.Get("StdRegProv"), ctx: ctx };
    } catch (e) {
        // No 64-bit provider (32-bit OS): use the default provider/view.
    }

    var services = locator.ConnectServer(".", "root\\default");
    return { provider: services.Get("StdRegProv"), ctx: null };
}

// Runs a StdRegProv method with the InParameters object, threading the 64-bit
// context (reg.ctx) into ExecMethod_ so the operation hits the native view.
function wmiExec(reg, methodName, inParams) {
    return reg.provider.ExecMethod_(methodName, inParams, 0, reg.ctx);
}

// StdRegProv.SetStringValue does NOT create the subkey and returns 2
// (ERROR_FILE_NOT_FOUND) when it does not already exist, so CreateKey first.
function regCreateKey(reg, key) {
    var inParams = reg.provider.Methods_.Item("CreateKey").InParameters.SpawnInstance_();
    inParams.hDefKey = HKLM;
    inParams.sSubKeyName = key;
    wmiExec(reg, "CreateKey", inParams);
}

function regWriteString(key, name, value) {
    var reg = getRegProv();
    regCreateKey(reg, key);
    var inParams = reg.provider.Methods_.Item("SetStringValue").InParameters.SpawnInstance_();
    inParams.hDefKey = HKLM;
    inParams.sSubKeyName = key;
    inParams.sValueName = name;
    inParams.sValue = value;
    wmiExec(reg, "SetStringValue", inParams);
}

function regDeleteValue(key, name) {
    try {
        var reg = getRegProv();
        var inParams = reg.provider.Methods_.Item("DeleteValue").InParameters.SpawnInstance_();
        inParams.hDefKey = HKLM;
        inParams.sSubKeyName = key;
        inParams.sValueName = name;
        wmiExec(reg, "DeleteValue", inParams);
    } catch (e) {
        // Value already absent: nothing to do.
    }
}

// Deferred custom action. Reconciles the three TLS-cert state markers so they
// describe the END state of this run, regardless of which state the previous
// install left behind.
//
// MSI cannot do this on its own: the user/service marker components and the
// Service\TlsCertsOption component all live under the single
// DuplicatiTlsCertsFeature and are merely gated by a <Condition> on
// DUPLICATI_SERVICE_SELECTED. When the feature stays Local across a
// service<->user transition the component action states never flip to Absent,
// so MSI neither tears down the stale markers nor writes the new one. This CA
// closes that gap by writing/deleting the values explicitly.
//
// The desired state is passed through CustomActionData (a deferred CA cannot
// read session properties) and is one of:
//   "user"    - user cert install:   write TlsCertsUserInstalled;
//                                     drop TlsCertsServiceInstalled +
//                                     Service\TlsCertsOption.
//   "service" - service cert install: write TlsCertsServiceInstalled +
//                                     Service\TlsCertsOption=install;
//                                     drop TlsCertsUserInstalled.
//   "none"    - certs not installed:  drop all three markers.
function ReconcileTlsCertMarkers() {
    try {
        var state = Session.Property("CustomActionData");
        if (state)
            state = state.replace(/^\s+|\s+$/g, "");

        if (state == "user") {
            regWriteString(TLS_INSTALLSTATE_KEY, "TlsCertsUserInstalled", "1");
            regDeleteValue(TLS_INSTALLSTATE_KEY, "TlsCertsServiceInstalled");
            regDeleteValue(TLS_SERVICE_KEY, "TlsCertsOption");
        } else if (state == "service") {
            regWriteString(TLS_INSTALLSTATE_KEY, "TlsCertsServiceInstalled", "1");
            regWriteString(TLS_SERVICE_KEY, "TlsCertsOption", "install");
            regDeleteValue(TLS_INSTALLSTATE_KEY, "TlsCertsUserInstalled");
        } else if (state == "none") {
            regDeleteValue(TLS_INSTALLSTATE_KEY, "TlsCertsUserInstalled");
            regDeleteValue(TLS_INSTALLSTATE_KEY, "TlsCertsServiceInstalled");
            regDeleteValue(TLS_SERVICE_KEY, "TlsCertsOption");
        }
    } catch (e) {
        // Marker reconciliation is best-effort: never block the install on it.
    }
}

// Deferred custom action. Deletes all three TLS-cert marker values on
// uninstall / TLS-feature removal. ReconcileTlsCertMarkers may have written
// these values outside MSI's component machinery, so MSI's RemoveRegistryValues
// would not remove them; this closes that gap. Best-effort.
function DeleteTlsCertMarkers() {
    try {
        regDeleteValue(TLS_INSTALLSTATE_KEY, "TlsCertsServiceInstalled");
        regDeleteValue(TLS_INSTALLSTATE_KEY, "TlsCertsUserInstalled");
        regDeleteValue(TLS_SERVICE_KEY, "TlsCertsOption");
    } catch (e) {
        // Best-effort: never block uninstall on marker cleanup.
    }
}

function featureListContains(value, feature) {
    if (!value)
        return false;

    var parts = value.split(",");
    for (var i = 0; i < parts.length; i++) {
        // Trim surrounding whitespace without relying on String.trim
        var part = parts[i].replace(/^\s+|\s+$/g, "");
        if (part == feature)
            return true;
    }
    return false;
}

// Decides whether the Duplicati Windows Service is selected for THIS run and
// stores the answer in DUPLICATI_SERVICE_SELECTED. The three TLS-cert marker
// components gate their <Condition> on this property, so it must reflect the
// *requested action state* of DuplicatiServiceFeature, not merely whether a
// previous install registered the service.
//
// Precedence (highest first):
//   1. An explicit REMOVE of DuplicatiServiceFeature (e.g. a Change that
//      unchecks the service box) means the service is NOT selected, even when
//      a previous install left PREVIOUS_SERVICE_INSTALLED=1. Without this the
//      property would stay "1" and the service cert markers would be re-asserted
//      while the user cert marker stays suppressed.
//   2. An explicit ADDLOCAL of DuplicatiServiceFeature, the UI/Upgrade hints
//      (INSTALLASSERVICE / LAUNCH_AS_SERVICE), or a prior service install mean
//      the service IS selected.
function EvaluateServiceSelection() {
    try {
        var installAsService = Session.Property("INSTALLASSERVICE");
        var launchAsService = Session.Property("LAUNCH_AS_SERVICE");
        var addLocal = Session.Property("ADDLOCAL");
        var remove = Session.Property("REMOVE");
        var previousService = Session.Property("PREVIOUS_SERVICE_INSTALLED");

        // An explicit removal of the service feature always wins: the user is
        // tearing the service down in this run, so it is not selected.
        if (featureListContains(remove, "DuplicatiServiceFeature") ||
            featureListContains(remove, "ALL")) {
            Session.Property("DUPLICATI_SERVICE_SELECTED") = "";
            return;
        }

        // Explicit (re)install of the service feature in this run.
        if (featureListContains(addLocal, "DuplicatiServiceFeature")) {
            Session.Property("DUPLICATI_SERVICE_SELECTED") = "1";
            return;
        }

        // UI selections, upgrade migrations, or a service install carried over
        // from a previous run that is not being removed here.
        if (installAsService == "true" || launchAsService == "1" || previousService == "1") {
            Session.Property("DUPLICATI_SERVICE_SELECTED") = "1";
            return;
        }
    } catch (e) {
        // Fail-safe to ensure installer doesn't block if an unexpected OS block occurs
    }
}
