backupApp.controller('RestoreDirectController', function ($rootScope, $scope, $location, Localization, AppService, AppUtils, SystemInfo, ServerStatus, DialogService) {

    $scope.SystemInfo = SystemInfo.watch($scope);
    $scope.AppUtils = AppUtils;
    $scope.ServerStatus = ServerStatus;
    $scope.serverstate = ServerStatus.watch($scope);

    $scope.connecting = false;

    $scope.HideEditUri = function() {
        $scope.EditUriState = false;
    };

    $scope.doConnect = function() {
        $scope.connecting = true;
        $scope.ConnectionProgress = Localization.localize('Registering temporary backup ...');

        var opts = {};
        var obj = {'Backup': {'TargetURL':  $scope.TargetURL } };

        if (($scope.EncryptionPassphrase || '') == '')
            opts['--no-encryption'] = 'true';
        else
            opts['passphrase'] = $scope.EncryptionPassphrase;

        if (!AppUtils.parse_extra_options($scope.ExtendedOptions, opts))
            return false;

        obj.Backup.Settings = [];
        for(var k in opts) {
            obj.Backup.Settings.push({
                Name: k,
                Value: opts[k]
            });
        }

        AppService.post('/backups?temporary=true', obj, {'headers': {'Content-Type': 'application/json'}}).then(
            function(resp) {

                $scope.ConnectionProgress = Localization.localize('Listing backup dates ...');
                $scope.BackupID = resp.data.ID;
                $scope.fetchBackupTimes();
            }, function(resp) {
                var message = resp.statusText;
                if (resp.data != null && resp.data.Message != null)
                    message = resp.data.Message;

                $scope.connecting = false;
                $scope.ConnectionProgress = '';
                DialogService.dialog(Localization.localize('Error'), Localization.localize('Failed to connect: {0}', message));
            }
        );
    };

    $scope.fetchBackupTimes = function() {
        AppService.get('/backup/' + $scope.BackupID + '/filesets').then(
            function(resp) {
                // Pass the filesets through a global variable
                if ($rootScope.filesets == null)
                    $rootScope.filesets = {};
                $rootScope.filesets[$scope.BackupID] = resp.data;
                $location.path('/restore/' + $scope.BackupID);
            },

            function(resp) {
                var message = resp.statusText;
                if (resp.data != null && resp.data.Message != null)
                    message = resp.data.Message;

                if (message == 'encrypted-storage')
                    message = Localization.localize('The target folder contains encrypted files, please supply the passphrase');

                $scope.connecting = false;
                $scope.ConnectionProgress = '';
                DialogService.dialog(Localization.localize('Error'), Localization.localize('Failed to connect: {0}', message));
            }
        );
    };
});
