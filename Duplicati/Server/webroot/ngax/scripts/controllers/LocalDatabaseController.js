backupApp.controller('LocalDatabaseController', function($scope, $routeParams, $location, Localization, AppService, DialogService, BackupList, AppUtils) {

    $scope.BackupID = $routeParams.backupid;
    
    function resetBackupItem(force) {
        var prev = $scope.DBPath;

        $scope.Backup = BackupList.lookup[$scope.BackupID];
        $scope.DBPath = null;
        if ($scope.Backup == null || $scope.Backup.Backup == null) {
            $scope.NoLocalDB = true;
        } else { 
            $scope.DBPath = $scope.Backup.Backup.DBPath;

            if ($scope.DBPath != prev || force)
                AppService.post('/filesystem/validate', {path: $scope.DBPath}).then(function(resp) {
                    $scope.NoLocalDB = false;
                }, function() {
                    $scope.NoLocalDB = true;
                });
        }
    };

    $scope.$on('backuplistchanged', resetBackupItem);
    resetBackupItem();

    $scope.doDelete = function(continuation) {
        DialogService.dialog(Localization.localize('Confirm delete'), Localization.localize('Do you really want to delete the local database for: {0}', $scope.Backup.Backup.Name), [Localization.localize('No'), Localization.localize('Yes')], function(ix) {
            if (ix == 1)
                AppService.post('/backup/' + $scope.BackupID + '/deletedb').then(
                    function() {
                        resetBackupItem(true);
                        if (continuation != null)
                            continuation();
                    },
                    function(resp) { 
                        resetBackupItem();
                        AppUtils.connectionError(Localization.localize('Failed to delete:') + ' ', resp); 
                    }
                );
        });
    };

    $scope.doRepair = function() {
        AppService.post('/backup/' + $scope.BackupID + '/repair');
        $location.path('/');
    };

    $scope.doDeleteAndRepair = function() {
        $scope.doDelete(function() {
            $scope.doRepair();
        });
    };

    $scope.doSave = function(continuation, move) {

        function doUpdate() {
            AppService.post('/backup/' + $scope.BackupID + '/' + (move ? 'movedb' : 'updatedb'), {path: $scope.DBPath}).then(
                function(resp) {
                    $scope.Backup.Backup.DBPath = $scope.DBPath;
                    resetBackupItem(true);

                    if (continuation != null)
                        continuation();

                }, AppUtils.connectionError(Localization.localize(move ? 'Move failed:' : 'Update failed:') + ' ')
            );
        };

        function doCheckTarget() {
            AppService.post('/filesystem/validate', {path: $scope.DBPath}).then(function(resp) {
                DialogService.dialog(Localization.localize('Existing file found'), Localization.localize('An existing file was found at the new location\nAre you sure you want the database to point to an existing file?'), [Localization.localize('Cancel'), Localization.localize('No'), Localization.localize('Yes')], function(ix) {
                    if (ix == 2) {
                        doUpdate();
                    }
                });
            }, function() {
                doUpdate();
            });            
        };

        if (move) {
            doUpdate();
        } else {
            if ($scope.NoLocalDB)
                doCheckTarget();
            else
                DialogService.dialog(Localization.localize('Updating with existing database'), Localization.localize('You are changing the database path away from an existing database.\nAre you sure this is what you want?'), [Localization.localize('Cancel'), Localization.localize('No'), Localization.localize('Yes')], function(ix) {
                    if (ix == 2)
                        doCheckTarget();
                });
        }
    };

    $scope.doSaveAndRepair = function() {
        $scope.doSave(function() {
            $scope.doRepair();
        });
    };

    $scope.doMove = function() {
        AppService.post('/filesystem/validate', {path: $scope.DBPath}).then(function(resp) {
            DialogService.dialog(Localization.localize('Cannot move to existing file'), Localization.localize('An existing file was found at the new location'));
        }, function() {
            $scope.doSave(null, true);
        });

    };


});
