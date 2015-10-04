backupApp.controller('HomeController', function ($scope, BackupList, AppService) {
    $scope.backups = BackupList.watch($scope);

    $scope.runBackup = function(id) {
    	AppService.post('/backup/' + id + '/run');
    };
});