backupApp.controller('AppController', function($scope, BrandingService, ServerStatus) {
    $scope.appName = BrandingService.appName;
    $scope.state = ServerStatus.watch($scope);
    $scope.localized = {};

    $scope.doReconnect = function() {
    	ServerStatus.reconnect();
    };
});