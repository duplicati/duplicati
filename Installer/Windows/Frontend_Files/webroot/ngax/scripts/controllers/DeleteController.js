backupApp.controller('DeleteController', function($scope, $routeParams, $location, gettextCatalog, CaptchaService, DialogService, ServerStatus, SystemInfo, BackupList, AppService, AppUtils) {
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
                AppService.post('/filesystem/validate', {path: $scope.DBPath}).then(function(resp) {
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
            CaptchaService.Authorize(
                gettextCatalog.getString('Confirm delete'), 
                gettextCatalog.getString('To confirm you want to delete all remote files for "{{name}}", please enter the word you see below', {name: $scope.Backup.Backup.Name}),
                'DELETE /backup/' + $scope.BackupID,
                function(token, answer) {
                    AppService.delete('/backup/' + $scope.BackupID + '?delete-local-db=' + $scope.DeleteLocalDatabase + '&delete-remote-files=' + $scope.DeleteRemoteFiles + '&captcha-token=' + token + '&captcha-answer=' + answer).then(function() {
                        $location.path('/');
                    }, AppUtils.connectionError);
                }
            );
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
