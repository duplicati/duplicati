backupApp.controller('RestoreDirectController', function ($rootScope, $scope, $location, AppService, AppUtils, SystemInfo, ServerStatus, DialogService, gettextCatalog) {

    $scope.SystemInfo = SystemInfo.watch($scope);
    $scope.AppUtils = AppUtils;
    $scope.ServerStatus = ServerStatus;
    $scope.serverstate = ServerStatus.watch($scope);

    $scope.CurrentStep = 0;
    $scope.connecting = false;

    $scope.nextPage = function() {
        $scope.CurrentStep = Math.min(1, $scope.CurrentStep + 1);
    };

    $scope.prevPage = function() {
        $scope.CurrentStep = Math.max(0, $scope.CurrentStep - 1);
    };

    $scope.setBuilduriFn = function(builduriFn) {
        $scope.builduri = builduriFn;
    };

    $scope.importUrl = function () {
        DialogService.textareaDialog('Import URL', 'Enter a Backup destination URL:', null, gettextCatalog.getString('Enter URL'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('OK')], null, function(btn, input) {
            if (btn == 1) {
                $scope.TargetURL = input;
            }
        });
    };

    $scope.copyUrlToClipboard = function () {
        $scope.builduri(function(res) {
            DialogService.textareaDialog('Copy URL', null, null, res, [gettextCatalog.getString('OK')], 'templates/copy_clipboard_buttons.html');
        });
    };

    $scope.doConnect = function() {
        function connect() {
            $scope.connecting = true;
            $scope.ConnectionProgress = gettextCatalog.getString('Registering temporary backup …');

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

                    $scope.ConnectionProgress = gettextCatalog.getString('Listing backup dates …');
                    $scope.BackupID = resp.data.ID;
                    $scope.fetchBackupTimes();
                }, function(resp) {
                    var message = resp.statusText;
                    if (resp.data != null && resp.data.Message != null)
                        message = resp.data.Message;

                    $scope.connecting = false;
                    $scope.ConnectionProgress = '';
                    DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
                }
            );
        }

        function checkForValidBackupDestination(continuation) {
            $scope.builduri(function(res) {
                $scope.TargetURL = res;
                continuation();
            });
            $scope.CurrentStep = 0;
        }

        checkForValidBackupDestination(connect);
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
                    message = gettextCatalog.getString('The target folder contains encrypted files, please supply the passphrase');

                $scope.connecting = false;
                $scope.ConnectionProgress = '';
                DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
            }
        );
    };

    if ($location.$$path.indexOf('/restoredirect-import') == 0 && $rootScope.importConfig != null)
    {
        $scope.TargetURL = $rootScope.importConfig.Backup.TargetURL;

        var tmpsettings = angular.copy($rootScope.importConfig.Backup.Settings);
        var res = {};
        for (var i = tmpsettings.length - 1; i >= 0; i--) {
            if (tmpsettings[i].Name == 'passphrase') {
                $scope.EncryptionPassphrase = tmpsettings[i].Value;
                tmpsettings.splice(i, 1);
            } else {
                res['--' + tmpsettings[i].Name] = tmpsettings[i].Value;
            }
        }

        $scope.showAdvanced = true;
        $scope.ExtendedOptions = AppUtils.serializeAdvancedOptions(res);
    }

});
