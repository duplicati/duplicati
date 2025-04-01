backupApp.controller('DeleteController', function($scope, $routeParams, $location, gettextCatalog, DialogService, ServerStatus, SystemInfo, BackupList, AppService, AppUtils) {
    $scope.BackupID = $routeParams.backupid;
    $scope.DeleteLocalDatabase = true;
    $scope.DeleteRemoteFiles = false;

    function resetBackupItem(force) {
        var prev = $scope.DBPath;

        $scope.Backup = BackupList.lookup[$scope.BackupID];
        $scope.DBPath = null;
        
        if ($scope.Backup == null || $scope.Backup.Backup == null) {
            $scope.NoLocalDB = true;
            $scope.DbUsedElsewhere = false;
        } else { 
            $scope.DBPath = $scope.Backup.Backup.DBPath;

            if ($scope.DBPath != prev || force)
                AppService.postJson('/filesystem/validate', {path: $scope.DBPath}).then(function(resp) {
                    $scope.NoLocalDB = false;
                }, function() {
                    $scope.NoLocalDB = true;
                });

            if ($scope.DBPath != prev || force)
                AppService.get('/backup/' + $scope.BackupID + '/isdbusedelsewhere', {path: $scope.DBPath}).then(function(resp) {
                    $scope.DbUsedElsewhere = resp.data.inuse;
                    // Default to not delete the db if others use it
                    if (resp.data.inuse)
                        $scope.DeleteLocalDatabase = false;

                }, function() {
                    $scope.DbUsedElsewhere = true;
                });
        }

        if ($scope.Backup != null && !$scope.hasRefreshedRemoteSize && ($scope.Backup.Backup.Metadata.TargetFilesCount == null || $scope.Backup.Backup.Metadata.TargetFilesCount <= 0))
        {
            $scope.hasRefreshedRemoteSize = true;
            AppService.post('/backup/' + $scope.BackupID + '/report-remote-size').then(
                function(resp) {

                    var taskid = resp.data.ID;
                    $scope.list_files_taskid = taskid;

                    ServerStatus.callWhenTaskCompletes(taskid, function() {

                    });
            }, AppUtils.connectionError);
        }

    };

    $scope.$on('backuplistchanged', resetBackupItem);
    resetBackupItem();

    $scope.doExport = function() {
        $location.path('/export/' + $scope.BackupID);
    };

    $scope.doDelete = function() {
        if ($scope.DeleteRemoteFiles)
        {
            const dlg = DialogService.htmlDialog(                
                gettextCatalog.getString('Confirm delete'), 
                'templates/confirmdelete.html',
                [gettextCatalog.getString('Cancel'), gettextCatalog.getString('Delete')],
                function(ix) {
                    if (ix == 1) {
                        AppService.delete('/backup/' + $scope.BackupID + '?delete-local-db=' + $scope.DeleteLocalDatabase + '&delete-remote-files=' + $scope.DeleteRemoteFiles).then(function() {
                            $location.path('/');
                        }, AppUtils.connectionError);
                    }
                },   
                null,
                function(index, text, cur) {
                    // Allow the user to cancel
                    if (index != 1)
                        return true;
        
                    // Compare case insensitive
                    if (cur.requiredword?.toLowerCase() != cur.typedword?.toLowerCase() || cur.typedword == '')
                    {
                        alert("Please enter the required phrase to confirm the deletion");
                        return false;
                    }
                    
                    return true;
                }
            );

            // Set the dialog data
            dlg.backupId = $scope.BackupID;
            dlg.backupname = $scope.Backup.Backup.Name;
            dlg.requiredword = ('delete ' + $scope.Backup.Backup.Name).toLowerCase();
        }
        else
        {
            DialogService.dialog(gettextCatalog.getString('Confirm delete'), gettextCatalog.getString('Do you really want to delete the backup: "{{name}}" ?', {name: $scope.Backup.Backup.Name}), [gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function(ix) {
                if (ix == 1) {
                    AppService.delete('/backup/' + $scope.BackupID + '?delete-local-db=' + $scope.DeleteLocalDatabase + '&delete-remote-files=' + $scope.DeleteRemoteFiles).then(function() {
                        $location.path('/');
                    }, AppUtils.connectionError);
                }
            });
        }
    };

    $scope.goBack = function() {
        $location.path('/');
    };

});
