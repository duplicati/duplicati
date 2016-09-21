backupApp.controller('ExportController', function($scope, $routeParams, Localization, AppService, DialogService) {
    $scope.ExportType = 'file';
    $scope.Connecting = false;
    $scope.BackupID = $routeParams.backupid;
    $scope.Completed = false;


    $scope.doExport = function() {
        if ($scope.UseEncryption && $scope.ExportType == 'file' && ($scope.Passphrase || '').trim().length == 0) {
            DialogService.dialog(Localization.localize('No passphrase entered'), Localization.localize('To export without a passphrase, uncheck the "Encrypt file" box'));
            return;
        }

        if ($scope.ExportType == 'commandline') {
            $scope.Connecting = true;
            AppService.get('/backup/' + $scope.BackupID + '/export?cmdline=true').then(
                function(resp) {
                    $scope.Connecting = false;
                    $scope.Completed = true;
                    $scope.CommandLine = resp.data.Command;
                }, 
                function(resp) {
                    $scope.Connecting = false;
                    var message = resp.statusText;
                    if (resp.data != null && resp.data.Message != null)
                        message = resp.data.Message;

                    DialogService.dialog(Localization.localize('Error'), Localization.localize('Failed to connect: {0}', message));
                }
            );
        } else {
            $scope.DownloadURL = AppService.get_export_url($scope.BackupID, $scope.Passphrase);
            $scope.Completed = true;
        }

    };

});
