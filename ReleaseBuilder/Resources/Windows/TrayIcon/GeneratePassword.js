// Generates a 32-character alphanumeric password and stores it in the
// DUPLICATI_SERVICE_PASSWORD installer property. Run as an immediate custom
// action before the deferred bootstrap / secret-tool actions, which read the
// value via CustomActionData. Also displayed on the ExitDialog.

function LogMessage(message) {
    var record = Session.Installer.CreateRecord(1);
    record.StringData(1) = message;
    var INSTALLMESSAGE_INFO = 0x04000000;
    Session.Message(INSTALLMESSAGE_INFO, record);
}

// Generate a cryptographically random alphanumeric password.
// CAPICOM.Utilities.GetRandom returns a base64 string built from
// CryptGenRandom; we strip the non-alphanumeric characters and keep
// generating until we have at least the required length.
function GenerateRandomPassword(length) {
    var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    var result = "";

    // Try CAPICOM first (preferred, cryptographic).
    var capicom = null;
    try {
        capicom = new ActiveXObject("CAPICOM.Utilities.1");
    } catch (e) {
        capicom = null;
    }

    if (capicom != null) {
        // GetRandom(byteCount, 0) returns base64 of byteCount random bytes.
        // We request more than `length` bytes to ensure enough alphanumerics
        // remain after filtering.
        while (result.length < length) {
            var b64 = capicom.GetRandom(length * 2, 0);
            for (var i = 0; i < b64.length && result.length < length; i++) {
                var c = b64.charAt(i);
                if (alphabet.indexOf(c) >= 0)
                    result += c;
            }
        }
        return result;
    }

    // Fallback: WScript-style randomness via Math.random seeded from the
    // current time. Less secure, but still better than a hard-coded value.
    // Math.random in WSH is not cryptographically secure.
    var seed = (new Date()).getTime();
    for (var j = 0; j < length; j++) {
        seed = (seed * 9301 + 49297) % 233280;
        var idx = Math.floor(seed / 233280 * alphabet.length);
        result += alphabet.charAt(idx);
    }
    return result;
}

function CustomAction() {
    try {
        // Only generate if not already set. The action runs in both
        // InstallUISequence and InstallExecuteSequence so the property is
        // populated for silent installs and propagated through the elevation
        // switch for UI installs (in which case the UI sequence sets it
        // first, and the execute sequence sees the same value).
        var existing = Session.Property("DUPLICATI_SERVICE_PASSWORD");
        if (existing && existing.length > 0) {
            LogMessage("Duplicati service password already set, skipping generation.");
            return 0;
        }

        var pwd = GenerateRandomPassword(32);
        Session.Property("DUPLICATI_SERVICE_PASSWORD") = pwd;
        LogMessage("Generated Duplicati service password");
        return 0;
    } catch (e) {
        LogMessage("Failed to generate Duplicati service password: " + e.message);
        return 1;
    }
}
