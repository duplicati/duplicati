backupApp.controller('AppController', function($scope, BrandingService, ServerStatus, PauseModal) {
    $scope.appName = BrandingService.appName;
    $scope.state = ServerStatus.watch($scope);
    $scope.localized = {};
    $scope.showModal = PauseModal.activate;
});