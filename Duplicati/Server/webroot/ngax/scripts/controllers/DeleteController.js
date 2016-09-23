backupApp.controller('DeleteController', function($scope, $routeParams, Localization, ServerStatus, SystemInfo, BackupList, AppService) {
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

});
