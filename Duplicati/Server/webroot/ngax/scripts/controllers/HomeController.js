backupApp.controller('HomeController', function ($scope, BackupList) {
    $scope.backups = BackupList.watch($scope);
});