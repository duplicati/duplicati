backupApp.controller('AppController', function($scope, BrandingService, ServerStatus, SystemInfo) {
    $scope.appName = BrandingService.appName;
    $scope.state = ServerStatus.watch($scope);
    $scope.systemInfo = SystemInfo.watch($scope);
    
    $scope.localized = {};

    $scope.doReconnect = function() {
    	ServerStatus.reconnect();
    };
});