// Writes the UpgradeDropServiceCerts sentinel that tells the nested uninstall
// of the OLD product (triggered by RemoveExistingProducts during a major
// upgrade) that it must remove the service TLS certificates while the old
// service binaries are still present. The matching RegistrySearch that seeds
// UPGRADE_DROP_SERVICE_CERTS reads the SAME HKCU path.
//
// Why HKCU (not HKLM): WriteUpgradeSentinel must run BEFORE RemoveExisting
// Products, which is scheduled early (before InstallInitialize), so it can only
// be an immediate CA. Immediate CAs run impersonated and non-elevated, so a
// write to HKLM\SOFTWARE silently fails - which is exactly why every HKLM
// attempt (WScript.Shell or 64-bit StdRegProv) never reached the nested
// uninstall. The installing user CAN write their own HKCU, and the nested
// uninstall's AppSearch resolves HKCU to the same user, so the sentinel round-
// trips reliably (confirmed empirically).
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
// RegistryValue elements in the marker components in Duplicati.wxs, which write
// to the native HKLM\SOFTWARE view (SoftwareKey, NOT Wow6432Node) on both
// architectures.
//
// The MSI custom-action script host is 32-bit, so on x64 Windows a naive
// WScript.Shell RegWrite would be redirected into Wow6432Node and diverge from
// where the components wrote the markers. To stay consistent we go through the
// WMI StdRegProv bound to the explicit 64-bit registry view (see getRegProv).
var HKLM = 0x80000000;
var TLS_INSTALLSTATE_KEY = "SOFTWARE\\DuplicatiTeam\\Duplicati\\InstallState";
var TLS_SERVICE_KEY = "SOFTWARE\\DuplicatiTeam\\Duplicati\\Service";

// Returns a StdRegProv for HKLM. On x64/ARM64 (where the MSI - and thus this
// 32-bit script host - targets the native 64-bit registry) we request the
// 64-bit WMI provider so writes are NOT redirected into Wow6432Node, matching
// where the MSI marker components write. On a genuine 32-bit OS that provider
// does not exist, so we fall back to the default (32-bit) provider, which is
// the native view there anyway.
function getRegProv() {
    var locator = new ActiveXObject("WbemScripting.SWbemLocator");

    try {
        var namedValueSet = new ActiveXObject("WbemScripting.SWbemNamedValueSet");
        namedValueSet.Add("__ProviderArchitecture", 64);
        namedValueSet.Add("__RequiredArchitecture", true);
        var services64 = locator.ConnectServer(
            ".", "root\\default", "", "", null, null, 0, namedValueSet);
        return services64.Get("StdRegProv");
    } catch (e) {
        // No 64-bit provider (32-bit OS): use the default provider/view.
    }

    var services = locator.ConnectServer(".", "root\\default");
    return services.Get("StdRegProv");
}

function regWriteString(reg, hive, key, name, value) {
    var method = reg.Methods_.Item("SetStringValue").InParameters.SpawnInstance_();
    method.hDefKey = hive;
    method.sSubKeyName = key;
    method.sValueName = name;
    method.sValue = value;
    reg.ExecMethod_("SetStringValue", method);
}

function regDeleteValue(reg, hive, key, name) {
    try {
        var method = reg.Methods_.Item("DeleteValue").InParameters.SpawnInstance_();
        method.hDefKey = hive;
        method.sSubKeyName = key;
        method.sValueName = name;
        reg.ExecMethod_("DeleteValue", method);
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

        var reg = getRegProv();

        if (state == "user") {
            regWriteString(reg, HKLM, TLS_INSTALLSTATE_KEY, "TlsCertsUserInstalled", "1");
            regDeleteValue(reg, HKLM, TLS_INSTALLSTATE_KEY, "TlsCertsServiceInstalled");
            regDeleteValue(reg, HKLM, TLS_SERVICE_KEY, "TlsCertsOption");
        } else if (state == "service") {
            regWriteString(reg, HKLM, TLS_INSTALLSTATE_KEY, "TlsCertsServiceInstalled", "1");
            regWriteString(reg, HKLM, TLS_SERVICE_KEY, "TlsCertsOption", "install");
            regDeleteValue(reg, HKLM, TLS_INSTALLSTATE_KEY, "TlsCertsUserInstalled");
        } else if (state == "none") {
            regDeleteValue(reg, HKLM, TLS_INSTALLSTATE_KEY, "TlsCertsUserInstalled");
            regDeleteValue(reg, HKLM, TLS_INSTALLSTATE_KEY, "TlsCertsServiceInstalled");
            regDeleteValue(reg, HKLM, TLS_SERVICE_KEY, "TlsCertsOption");
        }
    } catch (e) {
        // Marker reconciliation is best-effort: never block the install on it.
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
