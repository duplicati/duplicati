backupApp.controller('StateController', function($scope, ServerStatus, BackupList, AppService, AppUtils) {
    $scope.state = ServerStatus.watch($scope);
    $scope.backups = BackupList.watch($scope);

    $scope.activeTask = null;

    var updateActiveTask = function() {
        $scope.activeTask = $scope.state.activeTask == null ? null : BackupList.lookup[$scope.state.activeTask];
    };

    $scope.$on('backuplistchanged', updateActiveTask);
    $scope.$on('serverstatechanged', updateActiveTask);

    $scope.sendResume = function() {
    	ServerStatus.resume().then(function() {}, AppUtils.connectionError);
    };
});