backupApp.controller('LocalDatabaseController', function($scope, $routeParams, $location, AppService, DialogService, BackupList, AppUtils) {

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
        DialogService.dialog('Confirm delete', 'Do you really want to delete the local database for: ' + $scope.Backup.Backup.Name, ['No', 'Yes'], function(ix) {
            if (ix == 1)
                AppService.post('/backup/' + $scope.BackupID + '/deletedb').then(
                    function() {
                        resetBackupItem(true);
                        if (continuation != null)
                            continuation();
                    },
                    function(resp) { 
                        resetBackupItem();
                        AppUtils.connectionError('Failed to delete: ', resp); 
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

                }, AppUtils.connectionError((move ? 'Move' : 'Update') + ' failed: ')
            );
        };

        function doCheckTarget() {
            AppService.post('/filesystem/validate', {path: $scope.DBPath}).then(function(resp) {
                DialogService.dialog('Existing file found', 'An existing file was found at the new location\nAre you sure you want the database to point to an existing file?', ['Cancel', 'No', 'Yes'], function(ix) {
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
                DialogService.dialog('Updating with existing database', 'You are changing the database path away from an existing database.\nAre you sure this is what you want?', ['Cancel', 'No', 'Yes'], function(ix) {
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
            DialogService.dialog('Cannot move to existing file', 'An existing file was found at the new location');
        }, function() {
            $scope.doSave(null, true);
        });

    };


});