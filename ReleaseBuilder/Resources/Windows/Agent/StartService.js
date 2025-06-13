function LogMessage(message) {
    var record = Session.Installer.CreateRecord(1);
    record.StringData(1) = message;
    var INSTALLMESSAGE_INFO = 0x04000000;
    // MessageType 1 = Shown as warning in the UI
    Session.Message(INSTALLMESSAGE_INFO, record); // MessageType 1 = informational log
}

// Function to start a service
function StartService(serviceName) {
    var service;

    try {
        // Connect to the WMI service
        var locator = new ActiveXObject("WbemScripting.SWbemLocator");
        var root = locator.ConnectServer(".", "root\\CIMV2");

        // Query the service
        var query = "SELECT * FROM Win32_Service WHERE Name = '" + serviceName + "'";
        var services = root.ExecQuery(query);

        // Check if the service exists and start it if not already running
        var enumerator = new Enumerator(services);
        if (!enumerator.atEnd()) {
            service = enumerator.item();
            if (service.State != "Running") {
                service.StartService();
                LogMessage("Service '" + serviceName + "' started successfully.");
                return 0; // Success
            } else {
                LogMessage("Service '" + serviceName + "' is already running.");
                return 0; // Success
            }
        } else {
            LogMessage("Service '" + serviceName + "' not found.");
            return 1; // Failure
        }
    } catch (e) {
        LogMessage("Error starting service '" + serviceName + "': " + e.message);
        return 1; // Failure
    }
}

// Entry point for the custom action
function CustomAction() {
    return StartService("Duplicati.Agent");
}