function LogMessage(message) {
    var record = Session.Installer.CreateRecord(1);
    record.StringData(1) = message;
    var INSTALLMESSAGE_INFO = 0x04000000;
    // MessageType 1 = Shown as warning in the UI
    Session.Message(INSTALLMESSAGE_INFO, record); 
}

// Function to stop a Windows service
function StopService(serviceName) {
    var locator;
    var root;
    var query;
    var services;
    var enumerator;
    var service;

    try {
        // Connect to WMI (Windows Management Instrumentation)
        locator = new ActiveXObject("WbemScripting.SWbemLocator");
        root = locator.ConnectServer(".", "root\\CIMV2");

        // Query for the service by name
        query = "SELECT * FROM Win32_Service WHERE Name = '" + serviceName + "'";
        services = root.ExecQuery(query);

        // Enumerate the results
        enumerator = new Enumerator(services);
        if (!enumerator.atEnd()) {
            service = enumerator.item();

            // Check if the service is running
            if (service.State == "Running") {
                LogMessage("Service '" + serviceName + "' is running. Attempting to stop it.");
                
                // Attempt to stop the service
                var result = service.StopService();
                if (result === 0) {
                    LogMessage("Service '" + serviceName + "' stopped successfully.");
                } else {
                    LogMessage("Failed to stop service '" + serviceName + "'. Error code: " + result);
                }
            
                return 0; // Success
            } else {
                LogMessage("Service '" + serviceName + "' is not running.");
                return 0; // Success - nothing to stop
            }
        } else {
            LogMessage("Service '" + serviceName + "' not found.");
            return 0; // Success - service doesn't exist
        }
    } catch (e) {
        LogMessage("Error stopping service: " + e.message);
        return 1; // Failure
    }
}

// Entry point for the custom action
function CustomAction() {
    return StopService("Duplicati");
}