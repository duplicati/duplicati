backupApp.directive('waitArea', function() {
  return {
    restrict: 'E',
    scope: {
        taskid: '=taskid',
        text: '=text',
        allowCancel: '=allowCancel'
    },
    templateUrl: 'templates/waitarea.html',
    controller: function($scope, ServerStatus, AppService) {
        $scope.ServerStatus = ServerStatus;
        $scope.serverstate = ServerStatus.watch($scope);
        $scope.cancelTask = function() {
            AppService.post('/task/' + $scope.taskid + '/stop');
        };
    }
  }
});
