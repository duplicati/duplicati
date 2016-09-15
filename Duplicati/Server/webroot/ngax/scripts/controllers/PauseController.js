backupApp.controller('PauseController', function ($scope, $location, AppService, AppUtils, ServerStatus) {
    $scope.state = ServerStatus.watch($scope);

    $scope.pause = function(duration) {
        ServerStatus.pause(duration).then(function() {
            $location.path('/');
        }, AppUtils.connectionError);
    };

    $scope.resume = function() {
        ServerStatus.resume().then(function() {
            $location.path('/');
        }, AppUtils.connectionError);
    };
});
