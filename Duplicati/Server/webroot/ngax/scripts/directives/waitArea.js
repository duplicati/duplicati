backupApp.directive('waitArea', function() {
  return {
    restrict: 'E',
    scope: {
    	taskid: '=taskid',
    	text: '=text'
    },
    templateUrl: 'templates/waitarea.html',
    controller: function($scope, ServerStatus) {
    	$scope.ServerStatus = ServerStatus;
    	$scope.serverstate = ServerStatus.watch($scope);
    }
  }
});