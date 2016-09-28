backupApp.controller('UpdateChangelogController', function($scope, Localization, BrandingService, ServerStatus, AppService, AppUtils, SystemInfo) {
    $scope.brandingService = BrandingService.watch($scope);
    $scope.systeminfo = SystemInfo.watch($scope);
    $scope.serverstate = ServerStatus.watch($scope);

    function reloadChangeLog() {        
        AppService.get('/changelog?from-update=true').then(function(resp) {
            $scope.Version =  resp.data.Version;
            $scope.ChangeLog =  resp.data.Changelog;
        });        
    };

    $scope.doInstall = function() {
        AppService.post('/updates/install').then(function() {}, AppUtils.connectionError(Localization.localize('Install failed:') + ' '));
    };

    $scope.doActivate = function() {
        AppService.post('/updates/activate').then(function() {}, AppUtils.connectionError(Localization.localize('Activate failed:') + ' '));
    };

    $scope.doCheck = function() {
        AppService.post('/updates/check').then(function() {
            reloadChangeLog();
        }, AppUtils.connectionError(Localization.localize('Check failed:') + ' '));
    };

    reloadChangeLog();

});
