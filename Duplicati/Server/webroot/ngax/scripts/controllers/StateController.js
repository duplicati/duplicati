backupApp.controller('StateController', function($scope, $timeout, ServerStatus, BackupList, AppService, AppUtils) {
    $scope.state = ServerStatus.watch($scope);
    $scope.backups = BackupList.watch($scope);
    $scope.ServerStatus = ServerStatus;

    $scope.activeTask = null;

    var updateActiveTask = function() {
        $scope.activeBackup = $scope.state.activeTask == null ? null : BackupList.lookup[$scope.state.activeTask.Item2];
    };

    $scope.$on('backuplistchanged', updateActiveTask);
    $scope.$on('serverstatechanged', updateActiveTask);
    $scope.$on('serverstatechanged.pauseTimeRemain', function() { $timeout(function() {$scope.$digest(); }) });

    $scope.sendResume = function() {
    	ServerStatus.resume().then(function() {}, AppUtils.connectionError);
    };


});