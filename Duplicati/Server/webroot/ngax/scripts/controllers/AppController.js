backupApp.controller('AppController', function($scope, $cookies, AppService, BrandingService, ServerStatus, SystemInfo, AppUtils) {
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

    $scope.isLoggedIn = $cookies.get('session-auth') != null && $cookies.get('session-auth') != '';

    $scope.log_out = function() {
        AppService.log_out().then(function() {
            $cookies.remove('session-auth', { path: '/' });
            location.reload(true);            
        }, AppUtils.connectionError);
    };

});
