backupApp.controller('AppController', function($scope, BrandingService, ServerStatus, SystemInfo, AppUtils) {
    $scope.brandingService = BrandingService.watch($scope);
    $scope.state = ServerStatus.watch($scope);
    $scope.systemInfo = SystemInfo.watch($scope);
    
    $scope.localized = {};

    $scope.doReconnect = function() {
        ServerStatus.reconnect();
    };

    $scope.resume = function() {
        ServerStatus.resume().then(function() {}, AppUtils.connectionError);
    };

    $scope.pause = function(duration) {
        ServerStatus.pause(duration).then(function() {}, AppUtils.connectionError);
    };
});
