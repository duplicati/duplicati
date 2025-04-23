function LogMessage(message) {
    var record = Session.Installer.CreateRecord(1);
    record.StringData(1) = message;
    var INSTALLMESSAGE_INFO = 0x04000000;
    // MessageType 1 = Shown as warning in the UI
    Session.Message(INSTALLMESSAGE_INFO, record); 
}

// Function to check the status of a Windows service
function CheckServiceStatus(serviceName) {
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
            
                // Service was running and stopped
                Session.Property("SERVICE_WAS_STOPPED") = "1"; 
                return true;
            } else {
                LogMessage("Service '" + serviceName + "' is not running.");
                return false; // Service is not running
            }
        } else {
            LogMessage("Service '" + serviceName + "' not found.");
            return null; // Service not found
        }
    } catch (e) {
        LogMessage("Error checking service status: " + e.message);
        return null; // Error occurred
    }
}

// Entry point for the custom action
function CustomAction() {
    // Check the service status
    var isRunning = CheckServiceStatus("Duplicati");

    // Set a property in the installer to track the service status
    if (isRunning === true) {
        Session.Property("SERVICE_WAS_STOPPED") = "1"; // Service was running
    } else if (isRunning === false) {
        Session.Property("SERVICE_WAS_STOPPED") = "0"; // Service was not running
    } else {
        // Service not found or error occurred
        LogMessage("Failed to determine service status.");
        Session.Property("SERVICE_WAS_STOPPED") = "0"; // Service was not running
    }

    return 0; // Success
}