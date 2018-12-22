backupApp.controller("ExportController", function($scope, $routeParams, AppService, DialogService, gettextCatalog) {
    $scope.ExportType = "file";
    $scope.ExportPasswords = true;
    $scope.Connecting = false;
    $scope.BackupID = $routeParams.backupid;
    $scope.Completed = false;


    $scope.doExport = function() {
        // Helper function(s)
        function warnUnencryptedPasswords(continuation) {
            if ($scope.ExportType === "file" && $scope.ExportPasswords && !$scope.fileEncrypted) {
                DialogService.dialog(gettextCatalog.getString("Not using encryption"), gettextCatalog.getString("The configuration should be kept safe. Are you sure you want to save an unencrypted file containing your passwords?"), [gettextCatalog.getString("Cancel"), gettextCatalog.getString("Yes, I understand the risk")], function(ix) {
                    if (ix === 0) {
                        $scope.CurrentStep = 0;
                    } else {
                        continuation();
                    }
                });
            } else {
                continuation();
            }

        }

        // The actual export function to call after all checks pass
        function getExport() {
            if ($scope.ExportType === "commandline") {
                $scope.Connecting = true;
                AppService.get("/backup/" + $scope.BackupID + "/export?cmdline=true&export-passwords=" + encodeURIComponent($scope.ExportPasswords)).then(
                    function(resp) {
                        $scope.Connecting = false;
                        $scope.Completed = true;
                        $scope.CommandLine = resp.data.Command;
                    },
                    function(resp) {
                        $scope.Connecting = false;
                        var message = resp.statusText;
                        if (resp.data != null && resp.data.Message != null) {
                            message = resp.data.Message;
                        }

                        DialogService.dialog(gettextCatalog.getString("Error"), gettextCatalog.getString("Failed to connect: {{message}}", { message: message }));
                    }
                );
            } else {
                $scope.DownloadURL = AppService.get_export_url($scope.BackupID, $scope.UseEncryption ? $scope.Passphrase : null, $scope.ExportPasswords);
                $scope.Completed = true;
            }

        }

        // Make checks that do not require user input
        $scope.fileEncrypted = false;
        if ($scope.UseEncryption && $scope.ExportType === "file") {
            if (typeof $scope.Passphrase === "undefined" || $scope.Passphrase.trim().length === 0) {
                DialogService.dialog(gettextCatalog.getString("No passphrase entered"), gettextCatalog.getString("To export without a passphrase, uncheck the \"Encrypt file\" box"));
                return;
            } else if ($scope.Passphrase !== $scope.ConfirmPassphrase) {
                DialogService.dialog(gettextCatalog.getString("Error"), gettextCatalog.getString("The passwords do not match"));
                return;
            } else {
               $scope.fileEncrypted = true;
            }
        }

        // Chain various checks that require user input
        warnUnencryptedPasswords(function() { getExport(); });
    };
});
