backupApp.controller('PauseController', function ($scope, PauseModal, AppService, AppUtils, ServerStatus) {
    $scope.state = ServerStatus.watch($scope);

    $scope.closeMe = PauseModal.deactivate;

    $scope.pause = function(duration) {
    	ServerStatus.pause(duration).then(function() {
            PauseModal.deactivate();
        }, AppUtils.connectionError);
    };

    $scope.resume = function() {
    	ServerStatus.resume().then(function() {
            PauseModal.deactivate();
        }, AppUtils.connectionError);
    };
});